using System.Globalization;
using System.Text.Json;

public sealed record GamingCityProduct(string Id, string Name, string Code, string SupplierCode,
    string Sku, decimal? Price, decimal? Stock);
public sealed record GamingCityCatalog(GamingCityProduct[] Products, int Total, bool Truncated);

public sealed class GamingCityApi(HttpClient http, IConfiguration config)
{
    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["GamingCitySaaS:Iue"]);

    public static string ValidateSearch(string? search)
    {
        var value = search?.Trim() ?? "";
        if (value.Length is < 3 or > 80 || value.Any(char.IsControl))
            throw new ArgumentException("Ingresá entre 3 y 80 caracteres, sin caracteres de control.");
        return value;
    }

    public async Task<GamingCityCatalog> Query(string operation, string? search, CancellationToken ct)
    {
        if (operation != "products") throw new ArgumentException("Operación no habilitada para el catálogo.");
        var term = ValidateSearch(search);
        using var json = await Request("saas.php?action=productos&cantidad=completo&appname=descripcion-goodly&sinstock=1&busqueda=" +
            Uri.EscapeDataString(term), ct);
        var rows = ParseResults(json);
        if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Catálogo inválido.");
        // Only allowed product fields leave the server. The provider echoes IUE in solicitud.
        var products = rows.EnumerateArray().Take(100).Select(Product).ToArray();
        return new(products, rows.GetArrayLength(), rows.GetArrayLength() > products.Length);
    }

    public async Task<GamingCityProduct> ById(string id, CancellationToken ct)
    {
        if (id.Length is < 1 or > 20 || id.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("El ID de producto debe contener solamente números.");
        using var json = await Request("saasgateway.php/productos/" + id + "?iddeposito=1&appname=descripcion-goodly", ct);
        var product = Product(ParseResults(json));
        if (product.Id != id) throw new InvalidDataException("El proveedor devolvió otro producto.");
        return product;
    }

    private async Task<JsonDocument> Request(string path, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        ct = deadline.Token;
        if (!IsConfigured(config)) throw new InvalidOperationException("Falta configurar la credencial privada de Gaming City.");
        using var request = new HttpRequestMessage(HttpMethod.Get, path + "&iue=" + Uri.EscapeDataString(config["GamingCitySaaS:Iue"]!));
        request.Headers.UserAgent.ParseAdd("Gamexid/1.0");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Gaming City rechazó la consulta.", null, response.StatusCode);
        const int maxBytes = 8_000_000;
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Respuesta demasiado grande; afiná la búsqueda.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0) {
            if (buffer.Length + count > maxBytes) throw new InvalidDataException("Respuesta demasiado grande; afiná la búsqueda.");
            buffer.Write(chunk, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static JsonElement ParseResults(JsonDocument json)
    {
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("estado", out var state) ||
            state.ValueKind != JsonValueKind.True || !root.TryGetProperty("resultados", out var rows))
            throw new InvalidDataException("Gaming City devolvió un error o formato no confirmado.");
        return rows;
    }

    private GamingCityProduct Product(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Producto inválido.");
        string Field(string key, int max = 500) {
            if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "";
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw new InvalidDataException("Campo inválido.");
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (text.Length > max || text.Any(char.IsControl) || text.Contains(config["GamingCitySaaS:Iue"]!, StringComparison.Ordinal))
                throw new InvalidDataException("Campo no seguro.");
            return text;
        }
        decimal? Number(string key) => decimal.TryParse(Field(key, 40), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) ? value : null;
        var id = Field("idproducto", 80);
        var name = Field("nombre");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Producto sin identidad.");
        return new(id, name, Field("codigo", 100), Field("codigoproveedor", 1000), Field("sku", 100), Number("preciofinal"), Number("stockactual"));
    }

    private static async Task<IResult> Respond<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(new { source = "Gaming City", readOnly = true, data = await action() }); }
        catch (ArgumentException e) { return Results.BadRequest(new { mensaje = e.Message }); }
        catch (InvalidOperationException e) { return Results.Json(new { mensaje = e.Message }, statusCode: 503); }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.TooManyRequests) {
            return Results.Json(new { mensaje = "Gaming City limitó temporalmente las consultas. Esperá unos segundos y volvé a intentar." }, statusCode: 429);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OperationCanceledException) {
            return Results.Json(new { mensaje = "No se pudo consultar Gaming City. Reintentá con una búsqueda más específica." }, statusCode: 502);
        }
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/access/gc/status", (IConfiguration config) => Results.Ok(new {
            configured = IsConfigured(config), connected = false, readOnly = true,
            status = IsConfigured(config) ? "configured_unverified" : "missing_credentials"
        })).RequireAuthorization();
        app.MapGet("/api/access/gc/products", (string? search, GamingCityApi gc, CancellationToken ct) =>
            Respond(() => gc.Query("products", search, ct))).RequireAuthorization().RequireRateLimiting("gc");
        app.MapGet("/api/access/gc/products/{id}", (string id, GamingCityApi gc, CancellationToken ct) =>
            Respond(() => gc.ById(id, ct))).RequireAuthorization().RequireRateLimiting("gc");
    }
}
