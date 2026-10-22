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
        var result = await Connector(handler).Query("products", "RYZEN 5 3400G", default, onlyInStock: false);
        Assert.Equal("/gcapi/saas.php", handler.Uri!.AbsolutePath);
        Assert.Contains("cantidad=completo", handler.Uri.Query);
        Assert.Contains("sinstock=1", handler.Uri.Query);
        Assert.Contains("appname=gcportal", handler.Uri.Query);
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
        await Assert.ThrowsAsync<InvalidDataException>(() => Connector(handler).Query("products", "ryzen", default, onlyInStock: false));
    }
    [Theory]
    [InlineData("{\"estado\":false,\"resultados\":[]}")]
    [InlineData("{\"estado\":true,\"resultados\":{}}")]
    [InlineData("{\"estado\":true,\"resultados\":[{}]}")]
    public async Task RejectsInvalidCatalog(string body) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => Connector(new Handler(body)).Query("products", "ryzen", default, onlyInStock: false));
    [Fact]
    public async Task RejectsRedirectsAndMissingCredential()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Connector(new Handler("", HttpStatusCode.Redirect)).Query("products", "ryzen", default, onlyInStock: false));
        var handler = new Handler("");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Connector(handler, false).Query("products", "ryzen", default, onlyInStock: false));
        Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task LimitsResultsAndReportsTruncation()
    {
        var result = await Connector(new Handler("{\"estado\":true,\"resultados\":[" + string.Join(",", Enumerable.Repeat(Row, 101)) + "]}")).Query("products", "ryzen", default, onlyInStock: false);
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
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Connector(handler).Query("products", "ryzen", default, onlyInStock: false));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.DoesNotContain(Secret, error.Message);
    }
    private static GamingCityApi Portal(Handler handler, int accountBranch = 6) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://portal.gaming-city.com.ar/gcapi/") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["GamingCitySaaS:Iue"] = Secret,
            ["GamingCitySaaS:PortalUserBranchId"] = accountBranch.ToString(),
            ["GamingCitySaaS:Branches:0:Id"] = "1", ["GamingCitySaaS:Branches:0:Name"] = "Morón",
            ["GamingCitySaaS:Branches:0:DepotId"] = "4", ["GamingCitySaaS:Branches:0:Iue"] = "branch-private",
            ["GamingCitySaaS:Branches:1:Id"] = "3", ["GamingCitySaaS:Branches:1:Name"] = "Caballito",
            ["GamingCitySaaS:Branches:1:DepotId"] = "1", ["GamingCitySaaS:Branches:1:Iue"] = "caballito-private"
        }).Build());
    [Fact]
    public async Task PortalFiltersDiscontinuedZeroStockAndCombosBeforeCounting()
    {
        var available = Row.Replace("0.00", "5.00");
        var combo = available.Replace("private-cost", "private-cost").Replace("}", ",\"combo\":\"1\"}");
        var body = "{\"estado\":true,\"resultados\":[" + string.Join(",", new[] {
            available, Row, available.Replace("RYZEN 5 3400G", "xxx old"), combo }) + "]}";
        var handler = new Handler(body);
        var api = Portal(handler);
        var result = await api.Query("products", "ryzen", default, 1);
        Assert.Single(result.Products);
        Assert.Equal(1, result.Total);
        Assert.Equal(5m, result.Products[0].Stock);
        Assert.Contains("iddeposito=4", handler.Uri!.Query);
        Assert.Contains("iue=branch-private", handler.Uri.Query);
        Assert.DoesNotContain("sinstock", handler.Uri.Query);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(api.Branches()));
        result = await api.Query("products", "ryzen", default, 3, false, false);
        Assert.Equal(3, result.Total);
        Assert.Contains("iue=caballito-private", handler.Uri.Query);
        Assert.Contains("iddeposito=1", handler.Uri.Query);
        Assert.Contains("sinstock=1", handler.Uri.Query);
    }
    [Fact]
    public async Task UnknownBranchFailsWithoutCallingProvider()
    {
        var handler = new Handler("");
        await Assert.ThrowsAsync<ArgumentException>(() => Portal(handler).Query("products", "ryzen", default, 999));
        Assert.Null(handler.Uri);
    }
    [Fact]
    public async Task DetailUsesSelectedBranchAndRejectsDiscontinuedProducts()
    {
        var handler = new Handler("{\"estado\":true,\"resultados\":" + Row + "}");
        await Portal(handler).ById("4715", default, 1);
        Assert.Contains("iddeposito=4", handler.Uri!.Query);
        Assert.Contains("iue=branch-private", handler.Uri.Query);
        handler = new Handler("{\"estado\":true,\"resultados\":" + Row.Replace("RYZEN 5 3400G", "xxx old") + "}");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Portal(handler).ById("4715", default, 3));
    }
    [Theory]
    [InlineData(6, 1, "-3", 5)]
    [InlineData(3, 1, "-3", 0)]
    [InlineData(3, 1, "2", 3)]
    [InlineData(3, 3, "2", 5)]
    public async Task StockMatchesPortalAccountAndBranchRules(int account, int branch, string minimum, int expected)
    {
        var row = Row.Replace("0.00", "5.90").Replace("}", ",\"stockminimo\":\"" + minimum + "\"}");
        var api = Portal(new Handler("{\"estado\":true,\"resultados\":[" + row + "]}"), account);
        var result = await api.Query("products", "ryzen", default, branch, false);
        Assert.Equal(expected, result.Products[0].Stock);
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
