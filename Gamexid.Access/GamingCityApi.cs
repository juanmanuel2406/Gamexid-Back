using System.Text.Json;

public sealed class GamingCityApi(HttpClient http, IConfiguration config)
{
    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["GamingCity:AccessToken"]);

    public static string ValidateSearch(string? search)
    {
        var value = search?.Trim() ?? "";
        if (value.Length is < 3 or > 80 || value.Any(char.IsControl))
            throw new ArgumentException("Ingresá entre 3 y 80 caracteres, sin caracteres de control.");
        return value;
    }

    public async Task<JsonElement> Query(string operation, string? search, CancellationToken cancellation)
    {
        if (!IsConfigured(config)) throw new InvalidOperationException("GC-API requiere un access_token autorizado de GamingCity.");
        var token = config["GamingCity:AccessToken"]!;
        HttpRequestMessage request;
        if (operation == "branches")
            request = new(HttpMethod.Post, "sucursales") {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["action"] = "GetSucursales", ["access_token"] = token
                })
            };
        else if (operation == "products") {
            var term = ValidateSearch(search);
            request = new(HttpMethod.Get, "saas?action=productos&cantidad=20&busqueda=" +
                Uri.EscapeDataString(term) + "&access_token=" + Uri.EscapeDataString(token));
        }
        else throw new ArgumentException("Operación no habilitada.");
        using (request)
        using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)) {
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("GC-API rechazó la consulta.");
            if (response.Content.Headers.ContentLength > 1_000_000) throw new InvalidDataException("Respuesta demasiado grande.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellation)) != 0) {
                if (buffer.Length + count > 1_000_000) throw new InvalidDataException("Respuesta demasiado grande.");
                buffer.Write(chunk, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("response", out var state) ||
                state.ValueKind != JsonValueKind.String || state.GetString() != "ok" || !root.TryGetProperty("data", out var data))
                throw new InvalidDataException("GC-API devolvió un error o un formato no confirmado.");
            // Never forward an echoed credential, even inside an otherwise valid response.
            if (data.GetRawText().Contains(token, StringComparison.Ordinal) || ContainsSecretField(data))
                throw new InvalidDataException("La respuesta contiene campos sensibles y no puede mostrarse.");
            return data.Clone();
        }
    }

    private static bool ContainsSecretField(JsonElement element) => element.ValueKind switch {
        JsonValueKind.Object => element.EnumerateObject().Any(p =>
            new[] { "access_token", "appkey", "appsecret", "password", "psk", "token" }.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
            || ContainsSecretField(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsSecretField),
        _ => false
    };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/access/gc/status", (IConfiguration config) => Results.Ok(new {
            configured = IsConfigured(config), connected = false, readOnly = true,
            status = IsConfigured(config) ? "configured_unverified" : "missing_credentials",
            storage = "browser", contractDate = "2026-09-30"
        })).RequireAuthorization();
        app.MapGet("/api/access/gc/{operation}", async (string operation, string? search, GamingCityApi gc, CancellationToken ct) => {
            try { return Results.Ok(new { source = "GC-API", data = await gc.Query(operation, search, ct) }); }
            catch (ArgumentException e) { return Results.BadRequest(new { mensaje = e.Message }); }
            catch (InvalidOperationException e) { return Results.Json(new { mensaje = e.Message }, statusCode: 503); }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or JsonException or OperationCanceledException) {
                return Results.Json(new { mensaje = "No se pudo validar la respuesta de GC-API. No se modificaron datos." }, statusCode: 502);
            }
        }).RequireAuthorization().RequireRateLimiting("gc");
    }
}
