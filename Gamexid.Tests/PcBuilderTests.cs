using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Gamexid.Api.Controllers;
using Gamexid.Api.PcBuilder;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gamexid.Tests;

public class PcBuilderTests
{
    [Fact]
    public async Task ProductCacheAndPricesStayInTheirBranch()
    {
        using var h = new Fixture();
        var depot = await h.Service.Product("1", 6, default);
        var shop = await h.Service.Product("1", 3, default);
        Assert.Equal(8m, depot.Stock);
        Assert.Equal(2m, shop.Stock);
        await h.Service.Product("1", 6, default);
        Assert.Equal(2, h.Handler.Calls);
        var prices = await h.Service.Prices(7, "1", 6, default);
        var offer = Assert.Single(prices.Offers);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.Quote(7, [new("cpu", "1", OfferId: offer.Id)], 3, default));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.Quote(8, [new("cpu", "1", OfferId: offer.Id)], 6, default));
        var quote = await h.Service.Quote(7, [new("cpu", "1", OfferId: offer.Id)], 6, default);
        Assert.Equal(100m, Assert.Single(quote.Totals).Amount);
    }

    [Fact]
    public async Task MotherboardStepRequiresCpuWithoutCallingProvider()
    {
        using var h = new Fixture();
        var options = await h.Service.Options(new("mother", []), 3, "Caballito", default);
        Assert.Equal(new[] { "cpu" }, options.MissingSteps);
        Assert.Empty(options.Products);
        Assert.Equal(0, h.Handler.Calls);
    }

    [Fact]
    public async Task OptionsExcludeWrongSocketNoStockAndWrongCategory()
    {
        using var h = new Fixture();
        var options = await h.Service.Options(new("mother", [new("cpu", "1")]), 3, "Caballito", default);
        var candidate = Assert.Single(options.Products);
        Assert.Equal("2", candidate.Product.Id);
        Assert.Contains(candidate.Checks, c => c.Title.Contains("Socket") && c.Status == "checked");
        Assert.Contains(candidate.Checks, c => c.Title == "BIOS" && c.Status == "pending");
        Assert.Equal("pending", candidate.Status);
        Assert.All(h.Handler.Branches, b => Assert.Equal(3, b));
    }

    [Fact]
    public async Task ChangingCpuReevaluatesExistingMotherboard()
    {
        using var h = new Fixture();
        var options = await h.Service.Options(new("cpu", [new("cpu", "3"), new("mother", "2")]), 3, "Caballito", default);
        Assert.Equal("1", Assert.Single(options.Products).Product.Id);
    }

    [Fact]
    public async Task RamRequiresMatchingGenerationAndRequestedStock()
    {
        using var h = new Fixture();
        var options = await h.Service.Options(new("ram", [new("cpu", "1"), new("mother", "2")], Quantity: 2), 3, "Caballito", default);
        Assert.Equal("7", Assert.Single(options.Products).Product.Id);
        var insufficient = await h.Service.Options(new("ram", [new("cpu", "1"), new("mother", "2")], Quantity: 4), 3, "Caballito", default);
        Assert.Empty(insufficient.Products);
    }

    [Fact]
    public async Task UnknownSocketRemainsPendingAndCanBeExcluded()
    {
        using var h = new Fixture();
        h.Handler.UnknownSocket = true;
        var request = new PcStepRequest("mother", [new("cpu", "1")]);
        var options = await h.Service.Options(request, 3, "Caballito", default);
        Assert.Contains(options.Products, p => p.Product.Id == "2" && p.Status == "pending");
        Assert.Empty((await h.Service.Options(request with { IncludePending = false }, 3, "Caballito", default)).Products);
    }

    [Fact]
    public async Task OperatorCannotSelectAnotherBranch()
    {
        using var h = new Fixture();
        Assert.IsType<ForbidResult>(await h.Controller().Prices("1", default, 2));
        Assert.Equal(0, h.Handler.Calls);
    }

    [Fact]
    public async Task LocalBranchIdIsMappedByCodeNotNumericCoincidence()
    {
        using var h = new Fixture();
        var result = Assert.IsType<OkObjectResult>(await h.Controller().Options(new("cpu", [], BranchId: 13), default));
        var options = Assert.IsType<PcOptions>(result.Value);
        Assert.Equal(13, options.BranchId);
        Assert.Equal("Caballito", options.BranchName);
        Assert.All(h.Handler.Branches, b => Assert.Equal(3, b));
    }

    [Fact]
    public async Task LegacyRequestUsesAssignedBranchAndAdminCanSelectAnother()
    {
        using var h = new Fixture();
        Assert.IsType<OkObjectResult>(await h.Controller().Prices("1", default));
        Assert.All(h.Handler.Branches, b => Assert.Equal(3, b));
        h.Db.Users.Single().Role = UserRole.Administrator;
        await h.Db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await h.Controller().Prices("1", default, 2));
        Assert.Contains(6, h.Handler.Branches);
    }

    [Fact]
    public async Task UnmappedOrInactiveBranchNeverFallsBackToDepot()
    {
        using var h = new Fixture();
        h.Db.Branches.Single(b => b.Id == 13).Code = "NUEVA";
        await h.Db.SaveChangesAsync();
        var unavailable = Assert.IsType<ObjectResult>(await h.Controller().Prices("1", default, 13));
        Assert.Equal(503, unavailable.StatusCode);
        h.Db.Branches.Single(b => b.Id == 13).IsActive = false;
        await h.Db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await h.Controller().Prices("1", default, 13));
        Assert.Equal(0, h.Handler.Calls);
    }

    [Fact]
    public async Task CatalogAllowsBrowsingPagesAndScopesLocalBranch()
    {
        using var h = new Fixture();
        var controller = new CatalogController(h.Db, h.Catalog) { ControllerContext = h.Controller().ControllerContext };
        Assert.IsType<ForbidResult>(await controller.Products(2, default));
        var result = Assert.IsType<OkObjectResult>(await controller.Products(13, default, page: 2, pageSize: 2));
        var json = JsonSerializer.SerializeToElement(result.Value);
        var data = json.GetProperty("data");
        Assert.Equal(2, data.GetProperty("Page").GetInt32());
        Assert.Equal(2, data.GetProperty("Products").GetArrayLength());
        Assert.Equal(6, data.GetProperty("Total").GetInt32());
        Assert.All(h.Handler.Branches, b => Assert.Equal(3, b));
        Assert.IsType<BadRequestObjectResult>(await controller.Products(13, default, page: 0));
        Assert.False(h.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task DetailUsesExactProductFromBranchCatalogInsteadOfGateway()
    {
        using var h = new Fixture();
        h.Handler.IncompleteDetail = true;
        var product = await h.Catalog.ById("1", default, 3);
        Assert.Equal("1", product.Id);
        Assert.Equal(2m, product.Stock);
        Assert.Equal(1, h.Handler.Calls);
        Assert.All(h.Handler.Branches, b => Assert.Equal(3, b));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Catalog.ById("999", default, 3));
    }

    [Fact]
    public async Task LoadedOptionsReuseBranchProductWithoutRepeatingDetailRequest()
    {
        using var h = new Fixture();
        await h.Service.Options(new PcStepRequest("cpu", [], 13), 3, "Caballito", default);
        var calls = h.Handler.Calls;
        var product = await h.Service.Product("1", 3, default);
        Assert.Equal(2m, product.Stock);
        Assert.Equal(calls, h.Handler.Calls);
        var other = await h.Service.Product("1", 6, default);
        Assert.Equal(8m, other.Stock);
        Assert.Equal(calls + 1, h.Handler.Calls);
    }

    [Theory]
    [InlineData("MOTHER AM4", "MEMORIA DDR3", "incompatible")]
    [InlineData("MOTHER AM4", "MEMORIA DDR 3", "incompatible")]
    [InlineData("MOTHER AM4", "MEMORIA DDR-3", "incompatible")]
    [InlineData("MOTHER AM4", "MEMORIA DDR4", "checked")]
    [InlineData("MOTHER AM5", "MEMORIA DDR4", "incompatible")]
    [InlineData("MOTHER AM5", "MEMORIA DDR5", "checked")]
    [InlineData("MOTHER DDR4", "MEMORIA DDR3", "incompatible")]
    [InlineData("MOTHER SIN DATOS", "MEMORIA DDR3", "pending")]
    public void MemoryGenerationUsesPlatformAndRecognizesSpacing(string board, string ram, string status)
    {
        PcLine Line(string slot, string name) => new(slot, new("1", name, "", "", "", 100, 2), 1, null, null, null);
        var checks = PcCompatibility.Check([Line("mother", board), Line("ram", ram)]);
        Assert.Contains(checks, c => c.Title == "Generación de memoria" && c.Status == status);
        if (status != "incompatible") Assert.DoesNotContain(checks, c => c.Status == "incompatible");
    }

    [Fact]
    public async Task Ddr3IsFilteredAndCannotBeQuotedWithAm4EvenWithoutMotherboardDdrLabel()
    {
        using var h = new Fixture();
        h.Handler.LegacyRam = true;
        var selection = new PcSelection[] { new("cpu", "1"), new("mother", "2") };
        var options = await h.Service.Options(new("ram", selection), 3, "Caballito", default);
        Assert.Empty(options.Products);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => h.Service.Quote(7, [.. selection, new("ram", "7")], 3, default));
        Assert.Contains("DDR4", error.Message);
        Assert.Contains("DDR3", error.Message);
        Assert.Contains("No se puede generar el presupuesto", error.Message);
    }

    private sealed class Fixture : IDisposable
    {
        public GamexidDbContext Db = new(new DbContextOptionsBuilder<GamexidDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public PortalHandler Handler = new();
        public PcBuilderService Service;
        public GamingCityApi Catalog;
        private MemoryCache Cache = new(new MemoryCacheOptions());
        public Fixture()
        {
            Db.Branches.AddRange(new Branch { Id = 2, Code = "DEP-CENTRAL", Name = "Depósito" }, new Branch { Id = 13, Code = "GX-CABALLITO", Name = "Caballito" });
            Db.Users.Add(new User { Id = 7, FullName = "Operador", Email = "test@example.com", PasswordHash = "unused", BranchId = 13 });
            Db.SaveChanges();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["GamingCitySaaS:Iue"] = "test", ["PcBuilder:PortalCurrency"] = "ARS",
                ["GamingCitySaaS:Branches:0:Id"] = "6", ["GamingCitySaaS:Branches:0:Name"] = "Depósito",
                ["GamingCitySaaS:Branches:0:DepotId"] = "1", ["GamingCitySaaS:Branches:0:Iue"] = "test6",
                ["GamingCitySaaS:Branches:1:Id"] = "3", ["GamingCitySaaS:Branches:1:Name"] = "Caballito",
                ["GamingCitySaaS:Branches:1:DepotId"] = "1", ["GamingCitySaaS:Branches:1:Iue"] = "test3"
            }).Build();
            Catalog = new(new HttpClient(Handler) { BaseAddress = new("https://portal.gaming-city.com.ar/gcapi/") }, config);
            Service = new(Catalog, new HttpClient(), Cache, config);
        }
        public PcBuilderController Controller() => new(Service, Db, Catalog) { ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "test")) }
        } };
        public void Dispose() { Db.Dispose(); Cache.Dispose(); }
    }

    private sealed class PortalHandler : HttpMessageHandler
    {
        public int Calls;
        public bool UnknownSocket;
        public bool LegacyRam;
        public bool IncompleteDetail;
        public List<int> Branches = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var branch = request.RequestUri!.Query.Contains("test6") ? 6 : 3;
            Branches.Add(branch);
            object Row(string id, string name, int stock) => new { idproducto = id, nombre = name, codigo = "P" + id, stockactual = stock, preciofinal = 100, obstienda = "" };
            var rows = new[] {
                Row("1", "PROCESADOR AM4", branch == 6 ? 8 : 2),
                Row("2", LegacyRam ? "MOTHER AM4" : UnknownSocket ? "MOTHER SIN SOCKET DDR4" : "MOTHER AM4 DDR4", 2),
                Row("3", "PROCESADOR AM5", 2), Row("4", "MOTHER AM5 DDR5", 2), Row("5", "MOTHER AM4 DDR4", 0),
                Row("6", "MEMORIA DDR5", 3), Row("7", LegacyRam ? "MEMORIA DDR3" : "MEMORIA DDR4", 3)
            };
            object result = request.RequestUri.AbsolutePath.Contains("saasgateway")
                ? IncompleteDetail ? Row("1", "VGA RTX 4080 GDDR6", 10) : rows.Single(r => JsonSerializer.SerializeToElement(r).GetProperty("idproducto").GetString() == request.RequestUri.Segments.Last()) : rows;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { estado = true, resultados = result })) });
        }
    }
}
