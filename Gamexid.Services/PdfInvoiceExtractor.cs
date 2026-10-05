using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Gamexid.Services;

public sealed record InvoiceLine(string Ean, string? Sku, string Name, decimal? Expected, decimal? UnitPrice,
    decimal? Total, string? Currency, int Page, string RawText, string[] Warnings)
{
    public decimal? Confidence => null;
}
public sealed record InvoiceExtraction(string Source, InvoiceLine[] Lines, string[] Warnings, int Pages, bool RequiresOcr);
public sealed record PositionedWord(string Text, double X, double Y);

public sealed class PdfInvoiceExtractor
{
    public const int MaxBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(100);
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);

    public InvoiceExtraction Extract(byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length is < 5 or > MaxBytes || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new InvalidDataException("Seleccioná un PDF válido de hasta 10 MB.");
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages > 50) throw new InvalidDataException("El PDF admite hasta 50 páginas.");
        var timer = Stopwatch.StartNew();
        var lines = new List<InvoiceLine>();
        var warnings = new List<string>();
        var requiresOcr = false;
        for (var n = 1; n <= document.NumberOfPages; n++)
        {
            ct.ThrowIfCancellationRequested();
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new InvalidDataException("El PDF es demasiado complejo. Dividilo en archivos más pequeños.");
            var page = document.GetPage(n);
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Take(25001)
                .Select(w => new PositionedWord(w.Text, (double)w.BoundingBox.Left, (double)w.BoundingBox.Bottom)).ToArray();
            if (words.Length > 25000) throw new InvalidDataException("La página supera el límite de texto admitido.");
            if (words.Length == 0)
            {
                requiresOcr = true;
                warnings.Add($"Página {n}: sin texto seleccionable. Necesita OCR; no se inventaron productos.");
                continue;
            }
            var rows = new List<List<PositionedWord>>();
            foreach (var word in words.OrderByDescending(w => w.Y).ThenBy(w => w.X))
            {
                var row = rows.LastOrDefault();
                if (row == null || Math.Abs(row[0].Y - word.Y) > 3) rows.Add([word]);
                else row.Add(word);
            }
            Dictionary<string, double>? columns = null;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                var ordered = row.OrderBy(w => w.X).ToArray();
                var raw = string.Join(" ", ordered.Select(w => w.Text));
                var header = Header(ordered);
                if (header != null) { columns = header; continue; }
                var line = columns != null ? FromColumns(ordered, columns, n, raw) : FromLabels(raw, n);
                if (line != null) lines.Add(line);
                if (lines.Count > 1000) throw new InvalidDataException("El documento supera 1000 líneas de productos.");
            }
        }
        if (lines.Count == 0) warnings.Add("No se reconoció una tabla de productos. Cargá las líneas manualmente o adaptá el formato con un PDF de ejemplo.");
        if (lines.GroupBy(l => l.Ean.Length > 0 ? l.Ean : l.Sku).Any(g => g.Count() > 1))
            warnings.Add("Hay productos repetidos en el PDF. Revisá y unificá sus cantidades e importes; no se suman automáticamente.");
        warnings.Add("Revisá códigos, cantidades, precios, impuestos y descuentos contra el PDF antes de guardar. No se calcularon importes faltantes.");
        return new("pdfpig", lines.ToArray(), warnings.ToArray(), document.NumberOfPages, requiresOcr);
    }

    private static string Normal(string value) => new(value.ToLowerInvariant().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)).ToArray());

    private static Dictionary<string, double>? Header(PositionedWord[] words)
    {
        if (words.Any(w => Regex.IsMatch(w.Text, @"^\d"))) return null;
        var result = new Dictionary<string, double>();
        for (var i = 0; i < words.Length; i++)
        {
            var text = Normal(words[i].Text);
            var key = text switch {
                "ean" or "ean13" or "gtin" => "ean",
                "sku" or "codigo" or "cod" => "sku",
                "descripcion" or "producto" or "articulo" or "detalle" => "name",
                "cantidad" or "cant" or "qty" => "quantity",
                "precio" or "punit" or "punitario" or "preciounitario" or "unitario" => "price",
                "importe" or "subtotal" or "total" => "total",
                _ => null
            };
            if (key != null && !result.ContainsKey(key)) result[key] = words[i].X;
        }
        return (result.ContainsKey("ean") || result.ContainsKey("sku")) && result.ContainsKey("name") &&
            (result.ContainsKey("quantity") || result.ContainsKey("price")) ? result : null;
    }

    private static InvoiceLine? FromColumns(PositionedWord[] words, Dictionary<string, double> columns, int page, string raw)
    {
        if (words.Length == 0 || Regex.IsMatch(Normal(words[0].Text), "^(total|subtotal|iva|descuento|percepcion|neto|cuit|fecha)$")) return null;
        var order = columns.OrderBy(c => c.Value).ToArray();
        var values = order.ToDictionary(c => c.Key, _ => new List<string>());
        foreach (var word in words)
        {
            var column = order.LastOrDefault(c => word.X >= c.Value - 8);
            if (column.Key != null) values[column.Key].Add(word.Text);
        }
        string Read(string key) => values.TryGetValue(key, out var list) ? string.Join(" ", list) : "";
        var ean = Read("ean").Trim();
        var sku = Read("sku").Trim();
        if (ean.Length != 0 && !Regex.IsMatch(ean, @"^(?:\d{8}|\d{12,14})$")) ean = "";
        if (sku.Length > 40 || sku.Any(char.IsWhiteSpace)) sku = "";
        if (ean.Length == 0 && sku.Length == 0) return null;
        var name = Read("name").Trim();
        if (name.Length == 0) return null;
        return Make(ean, sku, name, Read("quantity"), Read("price"), Read("total"), raw, page);
    }

    public static InvoiceLine? FromLabels(string text, int page = 1)
    {
        var ean = Match(text, @"\b(?:EAN|GTIN)(?:-13)?\s*[:=]?\s*(\d{14}|\d{13}|\d{12}|\d{8})(?!\d)");
        var sku = Match(text, @"\bSKU\s*[:=]?\s*([A-Z0-9][A-Z0-9._/-]{0,39})(?=\s|$)");
        if (!ean.Success && !sku.Success) return null;
        var quantity = Match(text, @"\b(?:Cantidad|Cant\.?|Qty)\s*[:=]?\s*([0-9]+(?:[.,][0-9]+)?)");
        var price = Match(text, @"\b(?:Precio(?:\s+unitario)?|P\.?\s*Unit\.?)\s*[:=]?\s*(?:ARS|USD|\$)?\s*([0-9][0-9.,]*)");
        var total = Match(text, @"\b(?:Importe|Subtotal|Total)\s*[:=]?\s*(?:ARS|USD|\$)?\s*([0-9][0-9.,]*)");
        var description = Match(text, @"\b(?:Descripcion|Descripción|Producto)\s*[:=]?\s*(.*?)(?=\s+(?:Cantidad|Cant\.?|Qty|Precio|Importe|Total)\b|$)");
        return Make(ean.Groups[1].Value, sku.Groups[1].Value, description.Groups[1].Value,
            quantity.Groups[1].Value, price.Groups[1].Value, total.Groups[1].Value, text, page);
    }

    public static decimal? Number(string value)
    {
        var number = value.Trim().Replace("$", "").Replace("ARS", "", StringComparison.OrdinalIgnoreCase).Replace("USD", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!Regex.IsMatch(number, @"^[0-9]+(?:[.,][0-9]+)*$")) return null;
        if (number.Contains(',') && number.Contains('.'))
        {
            if (number.LastIndexOf(',') > number.LastIndexOf('.')) number = number.Replace(".", "").Replace(',', '.');
            else number = number.Replace(",", "");
        }
        else if (number.Contains(',')) number = number.Replace(',', '.');
        else if (Regex.IsMatch(number, @"^\d{1,3}(?:\.\d{3})+$")) number = number.Replace(".", "");
        return decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : null;
    }

    private static InvoiceLine Make(string ean, string sku, string name, string quantity, string price, string total, string raw, int page)
    {
        var expected = Number(quantity);
        var unitPrice = Number(price);
        var amount = Number(total);
        var warnings = new List<string>();
        if (expected is null || expected <= 0) { expected = null; warnings.Add("Cantidad pendiente de revisión."); }
        if (unitPrice == null) warnings.Add("Precio unitario no identificado.");
        if (name.Length == 0) warnings.Add("Descripción pendiente.");
        var currency = Match(raw, @"\bUSD\b|US\$").Success ? "USD" : Match(raw, @"\bARS\b").Success ? "ARS" : null;
        if (currency == null) warnings.Add("Moneda no confirmada; el símbolo $ no identifica por sí solo la moneda.");
        return new(ean, string.IsNullOrWhiteSpace(sku) ? null : sku, name, expected, unitPrice, amount, currency, page, raw, warnings.ToArray());
    }
}
