using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Gamexid.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    public ApiFactory() { connection.Open(); }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DatabaseConfiguration>();
            services.AddSingleton(new DatabaseConfiguration("test-only", "configured", new Version(8, 0, 36)));
            services.RemoveAll<DbContextOptions<GamexidDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<GamexidDbContext>>();
            services.AddDbContext<GamexidDbContext>(o => o.UseSqlite(connection));
        });
    }
    public async Task<HttpClient> Client(bool login = true)
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User { Id = 1, Email = "qa@example.test", FullName = "QA", PasswordHash = BCrypt.Net.BCrypt.HashPassword("test-only-password"), Role = UserRole.Administrator });
            db.Branches.Add(new Branch { Id = 2, Code = "DEP-CENTRAL", Name = "Morón (Depósito Central)" });
            db.Products.Add(new Product { Id = 1, Sku = "NOTE-001", Ean = "7790000000011", Name = "Notebook", RequiresSerialNumber = true });
            db.SerializedUnits.Add(new SerializedUnit { Id = 1, ProductId = 1, SerialNumber = "NB-10001", CurrentBranchId = 2 });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Gamexid", "1");
        if (login)
        {
            var result = await client.PostAsJsonAsync("/api/access/login", new { email = "qa@example.test", password = "test-only-password" });
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }
        return client;
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) connection.Dispose(); }
}

public class ApiTests
{
    [Theory]
    [InlineData("7790000000011", "ean", false)]
    [InlineData("note-001", "sku", false)]
    [InlineData("nb-10001", "serial", true)]
    public async Task ScannerResolvesEveryRegisteredIdentifier(string code, string kind, bool existing)
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        var response = await client.GetAsync("/api/products/lookup?code=" + code);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(kind, body.GetProperty("matchedBy").GetString());
        Assert.Equal(existing, body.GetProperty("alreadyInInventory").GetBoolean());
        Assert.Equal(1, body.GetProperty("product").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UnknownAndAmbiguousIdentifiersAreNotGuessed()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/products/lookup?code=UNKNOWN-SERIAL")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        db.Products.Add(new Product { Sku = "NB-10001", Name = "Conflicting", Ean = "12345678" });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/products/lookup?code=NB-10001")).StatusCode);
    }

    [Fact]
    public async Task ReceiptSavesMovementWithUnitLinkAndRejectsDuplicates()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        var dto = new ReceiptRequest(2, "QA", [new ReceiptLine(1, 1, ["NEW-SN"], true)]);
        var response = await client.PostAsJsonAsync("/api/inventory/receipts", dto);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movement = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(movement.GetProperty("items")[0].GetProperty("serializedUnitId").GetInt32() > 1);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/inventory/receipts", dto)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        Assert.Equal(1, await db.InventoryMovements.CountAsync());
        Assert.Equal(2, await db.SerializedUnits.CountAsync());
    }

    [Fact]
    public async Task RejectsIncompleteSerialsAndWrongDestination()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/inventory/receipts",
            new ReceiptRequest(2, null, [new ReceiptLine(1, 3, ["ONE"], true)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/inventory/receipts",
            new ReceiptRequest(999, null, [new ReceiptLine(1, 1, ["ONE"], true)]))).StatusCode);
    }

    [Fact]
    public async Task AnonymousAndMissingCsrfHeaderCannotWrite()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        client.DefaultRequestHeaders.Clear();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/products", new { sku = "BAD" })).StatusCode);
    }

    [Fact]
    public async Task PdfEndpointRequiresSessionAndExtractsWithoutExternalCalls()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(PdfAndConfigurationTests.Pdf("EAN 7790000000011 Producto Notebook Cantidad 2 Precio 100,00")), "file", "invoice.pdf");
        var result = await client.PostAsync("/api/documents/extract", form);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var data = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pdfpig", data.GetProperty("source").GetString());
        Assert.Equal(2, data.GetProperty("lines")[0].GetProperty("expected").GetInt32());
    }
}
