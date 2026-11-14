using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gamexid.Api.AI;
using Microsoft.Extensions.Caching.Memory;

namespace Gamexid.Api.PcBuilder;

public class PcBuilderService(GamingCityApi catalog, HttpClient http, IMemoryCache cache, IConfiguration config)
{
    private record StoredOffer(int UserId, int BranchId, string ProductId, PcOffer Offer, bool Confirmed);
    public async Task<GamingCityProduct> Product(string id, int branchId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, @"^[0-9]{1,20}$")) throw new ArgumentException("ID de producto inválido.");
        if (cache.TryGetValue<GamingCityProduct>("pc-product:" + branchId + ":" + id, out var saved)) return saved!;
        var product = await catalog.ById(id, ct, branchId: branchId);
        cache.Set("pc-product:" + branchId + ":" + id, product, TimeSpan.FromMinutes(2));
        return product;
    }
    private PcOffer Store(int user, int branchId, string product, string source, string title, decimal amount, string? currency, string? url, string? listing = null, bool confirmed = false)
    {
        var offer = new PcOffer(Guid.NewGuid().ToString(), source, title, amount, currency, url, DateTime.UtcNow, listing);
        cache.Set("pc-offer:" + offer.Id, new StoredOffer(user, branchId, product, offer, source != "MercadoLibre" || confirmed), TimeSpan.FromMinutes(15));
        return offer;
    }
    public async Task<PcPrices> Prices(int user, string id, int branchId, CancellationToken ct)
    {
        var product = await Product(id, branchId, ct);
        var offers = new List<PcOffer>(); var notices = new List<string>();
        var currency = config["PcBuilder:PortalCurrency"];
        if (currency is not ("ARS" or "USD")) currency = null;
        if (product.Price > 0) offers.Add(Store(user, branchId, id, "Portal", "Lista del portal", product.Price.Value, currency, null));
        if (currency == null) notices.Add("El portal no informa moneda: confirmala antes de sumar ese precio.");
        if (product.WebUrl != null && ProductPageReader.IsSafePage(product.WebUrl, product.Id)) {
            try {
                using var request = new HttpRequestMessage(HttpMethod.Get, product.WebUrl);
                var html = await Read(request, ct);
                var price = ParseWebPrice(html);
                if (price != null) offers.Add(Store(user, branchId, id, "Web", "Precio publicado en Gaming City", price.Value.Amount, price.Value.Currency, product.WebUrl));
                else notices.Add("La página no informa un precio y una moneda verificables.");
            } catch (Exception ex) when (SourceFailure(ex, ct)) { notices.Add("No se pudo consultar el precio de la página."); }
        }
        notices.Add("Precios sin envío ni armado. Verificá condiciones de pago en la fuente. Cotización válida durante 15 minutos.");
        return new(offers.ToArray(), notices.ToArray());
    }
    public static (decimal Amount, string Currency)? ParseWebPrice(string html)
    {
        string? Meta(string name) {
            foreach (Match tag in Regex.Matches(html, @"<meta\s[^>]{1,3000}>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150))) {
                if (!Regex.IsMatch(tag.Value, "(?:name|property)\\s*=\\s*[\"']" + Regex.Escape(name) + "[\"']", RegexOptions.IgnoreCase)) continue;
                return Regex.Match(tag.Value, "content\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase).Groups[1].Value;
            }
            return null;
        }
        var currency = Meta("product:price:currency");
        return currency is "ARS" or "USD" && decimal.TryParse(Meta("product:price:amount"), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) && amount > 0 && amount < 1_000_000_000 ? (amount, currency) : null;
    }
    public async Task<PcPrices> MercadoLibre(int user, string id, int branchId, CancellationToken ct)
    {
        var product = await Product(id, branchId, ct);
        if (string.IsNullOrWhiteSpace(config["MercadoLibre:AccessToken"]))
            return new([], ["Falta conectar una aplicación autorizada de Mercado Libre para buscar publicaciones automáticamente."]);
        try {
            using var request = MercadoRequest("sites/MLA/search?limit=8&q=" + Uri.EscapeDataString(product.Name[..Math.Min(100, product.Name.Length)]));
            using var json = JsonDocument.Parse(await Read(request, ct));
            if (!json.RootElement.TryGetProperty("results", out var results)) return new([], ["Mercado Libre no devolvió publicaciones."]);
            var offers = new List<PcOffer>();
            foreach (var item in results.EnumerateArray().Take(8)) {
                var offer = Listing(user, branchId, id, item);
                if (offer != null) offers.Add(offer);
            }
            return new(offers.ToArray(), ["Confirmá modelo, variante, vendedor y condición. El precio excluye envío y financiación."]);
        } catch (Exception ex) when (SourceFailure(ex, ct)) {
            return new([], ["Mercado Libre no autorizó o no pudo completar la búsqueda. Revisá conexión, permisos y vigencia del acceso."]);
        }
    }
    public async Task<PcOffer> ConfirmListing(int user, string id, string offerId, int branchId, CancellationToken ct)
    {
        var original = Offer(user, branchId, id, offerId);
        if (original.Source != "MercadoLibre" || original.ListingId == null) throw new ArgumentException("Seleccioná una publicación válida.");
        using var request = MercadoRequest("items/" + original.ListingId);
        using var json = JsonDocument.Parse(await Read(request, ct));
        var item = json.RootElement;
        if (!item.TryGetProperty("status", out var state) || state.GetString() != "active" ||
            item.GetProperty("id").GetString() != original.ListingId) throw new ArgumentException("La publicación ya no está activa.");
        return Listing(user, branchId, id, item, true) ?? throw new ArgumentException("La publicación no tiene precio verificable.");
    }
    private PcOffer? Listing(int user, int branchId, string product, JsonElement item, bool confirmed = false)
    {
        if (!item.TryGetProperty("id", out var id) || !Regex.IsMatch(id.GetString() ?? "", @"^MLA[0-9]{5,20}$") ||
            !item.TryGetProperty("price", out var price) || !price.TryGetDecimal(out var amount) || amount <= 0 || amount > 1_000_000_000 ||
            !item.TryGetProperty("currency_id", out var currency) || currency.GetString() is not ("ARS" or "USD") ||
            !item.TryGetProperty("permalink", out var link) || !SafeListing(link.GetString())) return null;
        var title = item.TryGetProperty("title", out var text) ? text.GetString() ?? "Publicación" : "Publicación";
        return Store(user, branchId, product, "MercadoLibre", title[..Math.Min(250, title.Length)], amount, currency.GetString(), link.GetString(), id.GetString(), confirmed);
    }
    public static bool SafeListing(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host is "articulo.mercadolibre.com.ar" or "www.mercadolibre.com.ar" && uri.IsDefaultPort && uri.UserInfo == "";
    private HttpRequestMessage MercadoRequest(string path) {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.mercadolibre.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["MercadoLibre:AccessToken"]);
        return request;
    }
    private PcOffer Offer(int user, int branchId, string product, string id, bool requireConfirmed = false) {
        if (!cache.TryGetValue<StoredOffer>("pc-offer:" + id, out var stored) || stored!.UserId != user || stored.BranchId != branchId || stored.ProductId != product)
            throw new ArgumentException("El precio venció o no corresponde a este producto. Volvé a consultar precios.");
        if (requireConfirmed && !stored.Confirmed) throw new ArgumentException("Confirmá la publicación de Mercado Libre antes de cotizar.");
        return stored.Offer;
    }
    public async Task<PcQuote> Quote(int user, PcSelection[] selections, int branchId, CancellationToken ct)
    {
        if (!PcCompatibility.Valid(selections)) throw new ArgumentException("Revisá componentes y cantidades.");
        var lines = new List<PcLine>();
        foreach (var selection in selections) {
            var product = await Product(selection.ProductId, branchId, ct);
            var offer = selection.OfferId == null ? null : Offer(user, branchId, product.Id, selection.OfferId, true);
            var currency = offer?.Currency ?? selection.ConfirmedCurrency;
            lines.Add(new(selection.Slot, product, selection.Quantity, offer, currency, offer == null || currency == null ? null : offer.Amount * selection.Quantity));
        }
        var checks = PcCompatibility.Check(lines.ToArray());
        var incompatible = checks.Where(c => c.Status == "incompatible").ToArray();
        if (incompatible.Length > 0)
            throw new ArgumentException("No se puede generar el presupuesto: " + string.Join(" ", incompatible.Select(c => c.Detail)));
        var totals = lines.Where(l => l.Subtotal != null).GroupBy(l => l.Currency!)
            .Select(g => new PcTotal(g.Key, g.Sum(l => l.Subtotal!.Value))).ToArray();
        return new(lines.ToArray(), checks, totals, lines.Count(l => l.Subtotal == null), DateTime.UtcNow);
    }
    public async Task<PcOptions> Options(PcStepRequest request, int branchId, string branchName, CancellationToken ct)
    {
        if (!PcCompatibility.Valid(request.Items) || !PcCompatibility.Slots.Contains(request.Slot) ||
            request.Quantity < 1 || request.Quantity > (request.Slot is "ram" or "storage" ? 4 : 1))
            throw new ArgumentException("Revisá el paso, los componentes y las cantidades.");
        var term = string.IsNullOrWhiteSpace(request.Search) ? PcCompatibility.SearchTerm(request.Slot) : GamingCityApi.ValidateSearch(request.Search);
        var selections = request.Items.Where(i => i.Slot != request.Slot).ToArray();
        var missing = PcCompatibility.Prerequisites(request.Slot).Where(slot => !selections.Any(i => i.Slot == slot)).ToArray();
        if (missing.Length > 0) return new(0, branchName, request.Slot, [], false, missing, [], DateTime.UtcNow);
        var lines = new List<PcLine>();
        foreach (var selection in selections)
            lines.Add(new(selection.Slot, await Product(selection.ProductId, branchId, ct), selection.Quantity, null, null, null));
        var selectionChecks = PcCompatibility.Check(lines.ToArray());
        var catalogResult = await catalog.Query("products", term, ct, branchId, onlyInStock: true, hideCombos: true);
        var options = new List<PcOption>();
        foreach (var product in catalogResult.Products)
        {
            if (PcCompatibility.Category(product.Name) != request.Slot || product.Stock is null || product.Stock < request.Quantity) continue;
            var checks = PcCompatibility.Check([.. lines, new(request.Slot, product, request.Quantity, null, null, null)]);
            if (checks.Any(c => c.Status == "incompatible")) continue;
            var relevant = checks.Where(c => c.Title != "Componente pendiente").ToArray();
            var status = relevant.Any(c => c.Status == "pending") ? "pending" : "checked";
            if (request.IncludePending || status == "checked") {
                cache.Set("pc-product:" + branchId + ":" + product.Id, product, TimeSpan.FromMinutes(2));
                options.Add(new(product, status, relevant));
            }
        }
        return new(0, branchName, request.Slot, options.ToArray(), catalogResult.Truncated, [], selectionChecks, DateTime.UtcNow);
    }

    private static bool SourceFailure(Exception ex, CancellationToken ct) => ex is HttpRequestException or IOException or InvalidDataException or JsonException or RegexMatchTimeoutException ||
        ex is OperationCanceledException && !ct.IsCancellationRequested;
    private async Task<string> Read(HttpRequestMessage request, CancellationToken ct) {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1_500_000) throw new IOException();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, ct)) > 0) {
            if (buffer.Length + count > 1_500_000) throw new IOException();
            buffer.Write(bytes, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
