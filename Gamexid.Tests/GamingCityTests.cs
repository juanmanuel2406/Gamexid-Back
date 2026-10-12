using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gamexid.Tests;

public class GamingCityTests
{
    private const string Secret = "test-private-iue";
    private const string Row = """{"idproducto":"4715","nombre":"RYZEN 5 3400G","codigo":"04715","codigoproveedor":"730143309837 730143313162","sku":"730143309837","preciofinal":"119999.00","stockactual":"0.00","costo":"private-cost"}""";
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            Assert.Equal("Gamexid/1.0", request.Headers.UserAgent.ToString());
            Assert.Equal("application/json", request.Headers.Accept.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
    private static GamingCityApi Connector(Handler handler, bool configured = true) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://portal.gaming-city.com.ar/gcapi/") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GamingCitySaaS:Iue"] = configured ? Secret : null }).Build());

    [Fact]
    public async Task CatalogUsesConfirmedContractAndNeverReturnsEchoedSecret()
    {
        var handler = new Handler("{\"estado\":true,\"solicitud\":{\"iue\":\"" + Secret + "\"},\"resultados\":[" + Row + "]}");
        var result = await Connector(handler).Query("products", "RYZEN 5 3400G", default);
        Assert.Equal("/gcapi/saas.php", handler.Uri!.AbsolutePath);
        Assert.Contains("cantidad=completo", handler.Uri.Query);
        Assert.Contains("sinstock=1", handler.Uri.Query);
        Assert.Contains("appname=descripcion-goodly", handler.Uri.Query);
        Assert.Contains("iue=" + Secret, handler.Uri.Query);
        Assert.Equal("730143309837 730143313162", result.Products[0].SupplierCode);
        Assert.Equal(0m, result.Products[0].Stock);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        Assert.DoesNotContain("private-cost", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task ByIdUsesGatewayAndDepositOne()
    {
        var handler = new Handler("{\"estado\":true,\"resultados\":" + Row + "}");
        var result = await Connector(handler).ById("4715", default);
        Assert.Equal("4715", result.Id);
        Assert.Equal("/gcapi/saasgateway.php/productos/4715", handler.Uri!.AbsolutePath);
        Assert.Contains("iddeposito=1", handler.Uri.Query);
    }
    [Fact]
    public async Task RejectsIdentityMismatchAndSecretInsideAllowedField()
    {
        var handler = new Handler("{\"estado\":true,\"resultados\":" + Row + "}");
        await Assert.ThrowsAsync<InvalidDataException>(() => Connector(handler).ById("123", default));
        handler = new Handler("{\"estado\":true,\"resultados\":[" + Row.Replace("RYZEN 5 3400G", Secret) + "]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => Connector(handler).Query("products", "ryzen", default));
    }
    [Theory]
    [InlineData("{\"estado\":false,\"resultados\":[]}")]
    [InlineData("{\"estado\":true,\"resultados\":{}}")]
    [InlineData("{\"estado\":true,\"resultados\":[{}]}")]
    public async Task RejectsInvalidCatalog(string body) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Connector(new Handler(body)).Query("products", "ryzen", default));
    [Fact]
    public async Task RejectsRedirectsAndMissingCredential()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Connector(new Handler("", HttpStatusCode.Redirect)).Query("products", "ryzen", default));
        var handler = new Handler("");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Connector(handler, false).Query("products", "ryzen", default));
        Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task LimitsResultsAndReportsTruncation()
    {
        var result = await Connector(new Handler("{\"estado\":true,\"resultados\":[" + string.Join(",", Enumerable.Repeat(Row, 101)) + "]}")).Query("products", "ryzen", default);
        Assert.Equal(100, result.Products.Length);
        Assert.Equal(101, result.Total);
        Assert.True(result.Truncated);
    }
    [Theory]
    [InlineData("../4715")]
    [InlineData("4715?iue=other")]
    [InlineData("")]
    public async Task RejectsInvalidIdBeforeRequest(string id)
    {
        var handler = new Handler("");
        await Assert.ThrowsAsync<ArgumentException>(() => Connector(handler).ById(id, default));
        Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task PreservesProviderRateLimitWithoutExposingItsBody()
    {
        var handler = new Handler(Secret, HttpStatusCode.TooManyRequests);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Connector(handler).Query("products", "ryzen", default));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.DoesNotContain(Secret, error.Message);
    }
    [Fact]
    public async Task CatalogEndpointsRequireAuthentication()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/access/gc/products?search=ryzen")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/access/gc/products/4715")).StatusCode);
    }
}
