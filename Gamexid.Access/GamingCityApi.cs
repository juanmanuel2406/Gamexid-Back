using System.Globalization;
using System.Text.Json;

public sealed record GamingCityProduct(string Id, string Name, string Code, string SupplierCode,
    string Sku, decimal? Price, decimal? Stock);
public sealed record GamingCityBranch(int Id, string Name, int DepotId);
public sealed record GamingCityCatalog(GamingCityProduct[] Products, int Total, bool Truncated);

public sealed class GamingCityApi(HttpClient http, IConfiguration config)
{
    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["GamingCitySaaS:Iue"]);

    public GamingCityBranch[] Branches() => config.GetSection("GamingCitySaaS:Branches").GetChildren()
        .Select(b => new GamingCityBranch(b.GetValue<int>("Id"), b["Name"] ?? "", b.GetValue<int>("DepotId")))
        .Where(b => b.Id > 0 && b.DepotId > 0).ToArray();

    private (GamingCityBranch Branch, string Key) ResolveBranch(int? branchId)
    {
        if (!IsConfigured(config)) throw new InvalidOperationException("Falta configurar la credencial privada de Gaming City.");
        var branches = Branches();
        if (branches.Length == 0) {
            if (branchId.HasValue) throw new ArgumentException("Sucursal del portal no configurada.");
            return (new(6, "Depósito", 1), config["GamingCitySaaS:Iue"]!);
        }
        var branch = branches.FirstOrDefault(b => b.Id == (branchId ?? 6))
            ?? throw new ArgumentException("Seleccioná una sucursal válida del portal.");
        var key = config.GetSection("GamingCitySaaS:Branches").GetChildren()
            .First(b => b.GetValue<int>("Id") == branch.Id)["Iue"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Sucursal del portal sin credencial configurada.");
        return (branch, key);
    }

    private static string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value)
        ? value.ToString() : "";
    private static decimal Numeric(JsonElement row, string key) => decimal.TryParse(Text(row, key),
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private decimal AvailableStock(JsonElement row, int branchId)
    {
        var stock = decimal.Truncate(Numeric(row, "stockactual"));
        var accountBranch = config.GetValue<int?>("GamingCitySaaS:PortalUserBranchId") ?? 6;
        if (accountBranch is not (-1 or 6)) {
            var minimum = branchId is 1 or 6 ? decimal.Truncate(Numeric(row, "stockminimo")) : 0;
            stock = minimum == -3 ? 0 : stock - minimum;
        }
        return stock;
    }
    private bool Visible(JsonElement row, int branchId, bool onlyInStock, bool hideCombos)
    {
        if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Producto inválido.");
        if (Text(row, "nombre").Contains("xxx", StringComparison.OrdinalIgnoreCase)) return false;
        var combo = Text(row, "combo") == "1";
        return (!hideCombos || !combo) && (!onlyInStock || AvailableStock(row, branchId) > 0 ||
            (combo && branchId is 1 or 6 && Numeric(row, "stockdinamico") > 0));
    }

    public static string ValidateSearch(string? search)
    {
        var value = search?.Trim() ?? "";
        if (value.Length is < 3 or > 80 || value.Any(char.IsControl))
            throw new ArgumentException("Ingresá entre 3 y 80 caracteres, sin caracteres de control.");
        return value;
    }

    public async Task<GamingCityCatalog> Query(string operation, string? search, CancellationToken ct,
        int? branchId = null, bool onlyInStock = true, bool hideCombos = true)
    {
        if (operation != "products") throw new ArgumentException("Operación no habilitada para el catálogo.");
        var term = ValidateSearch(search);
        var (branch, key) = ResolveBranch(branchId);
        var limit = term.Length >= 4 ? "completo" : "200";
        using var json = await Request($"saas.php?action=productos&cantidad={limit}&appname=gcportal&iddeposito={branch.DepotId}" +
            (onlyInStock ? "" : "&sinstock=1") + "&busqueda=" + Uri.EscapeDataString(term), key, ct);
        var rows = ParseResults(json);
        if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Catálogo inválido.");
        var visible = rows.EnumerateArray().Select(row => new { Row = row, Product = Product(row, branch.Id) })
            .Where(item => Visible(item.Row, branch.Id, onlyInStock, hideCombos)).ToArray();
        var products = visible.Select(item => item.Product).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
        return new(products, visible.Length, visible.Length > products.Length);
    }

    public async Task<GamingCityProduct> ById(string id, CancellationToken ct, int? branchId = null)
    {
        if (id.Length is < 1 or > 20 || id.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("El ID de producto debe contener solamente números.");
        var (branch, key) = ResolveBranch(branchId);
        using var json = await Request("saasgateway.php/productos/" + id + $"?iddeposito={branch.DepotId}&appname=gcportal", key, ct);
        var row = ParseResults(json);
        var product = Product(row, branch.Id);
        if (product.Id != id) throw new InvalidDataException("El proveedor devolvió otro producto.");
        if (!Visible(row, branch.Id, false, false)) throw new KeyNotFoundException("Producto fuera del catálogo vigente.");
        return product;
    }

    private async Task<JsonDocument> Request(string path, string key, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        ct = deadline.Token;
        if (!IsConfigured(config)) throw new InvalidOperationException("Falta configurar la credencial privada de Gaming City.");
        using var request = new HttpRequestMessage(HttpMethod.Get, path + "&iue=" + Uri.EscapeDataString(key));
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

    private GamingCityProduct Product(JsonElement row, int branchId)
    {
        if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Producto inválido.");
        string Field(string key, int max = 500) {
            if (!row.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "";
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw new InvalidDataException("Campo inválido.");
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (text.Length > max || text.Any(char.IsControl) || config.GetSection("GamingCitySaaS").AsEnumerable().Any(p =>
                p.Key.EndsWith(":Iue", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(p.Value) && text.Contains(p.Value, StringComparison.Ordinal)))
                throw new InvalidDataException("Campo no seguro.");
            return text;
        }
        decimal? Number(string key) => decimal.TryParse(Field(key, 40), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) ? value : null;
        var id = Field("idproducto", 80);
        var name = Field("nombre");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Producto sin identidad.");
        return new(id, name, Field("codigo", 100), Field("codigoproveedor", 1000), Field("sku", 100), Number("preciofinal"), row.TryGetProperty("stockactual", out _) ? AvailableStock(row, branchId) : null);
    }

    private static async Task<IResult> Respond<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(new { source = "Gaming City", readOnly = true, data = await action() }); }
        catch (KeyNotFoundException e) { return Results.NotFound(new { mensaje = e.Message }); }
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
        app.MapGet("/api/access/gc/branches", (GamingCityApi gc) => Results.Ok(gc.Branches())).RequireAuthorization();
        app.MapGet("/api/access/gc/products", (string? search, int? branchId, bool? onlyInStock, bool? hideCombos, GamingCityApi gc, CancellationToken ct) =>
            Respond(() => gc.Query("products", search, ct, branchId, onlyInStock ?? true, hideCombos ?? true))).RequireAuthorization().RequireRateLimiting("gc");
        app.MapGet("/api/access/gc/products/{id}", (string id, int? branchId, GamingCityApi gc, CancellationToken ct) =>
            Respond(() => gc.ById(id, ct, branchId))).RequireAuthorization().RequireRateLimiting("gc");
    }
}
