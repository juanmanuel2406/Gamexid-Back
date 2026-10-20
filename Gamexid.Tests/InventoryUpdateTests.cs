using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gamexid.Tests;

public class InventoryUpdateTests
{
    [Fact]
    public async Task SimilarScanSuggestsWithoutWritingOrAssigning()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        var response = await client.GetAsync("/api/products/lookup?code=note-002");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("suggestions")[0].GetProperty("product").GetProperty("id").GetInt32());
        Assert.False(body.TryGetProperty("product", out _));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        Assert.Equal(1, await db.Products.CountAsync());
        Assert.Equal(1, await db.SerializedUnits.CountAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task StockRequiresBranchSearchesCaseInsensitivelyAndCountsBothKinds()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
            db.Branches.Add(new Branch { Id = 3, Code = "CAB", Name = "Caballito" });
            db.Products.Add(new Product { Id = 2, Sku = "CABLE", Ean = "12345678", Name = "Cable", RequiresSerialNumber = false });
            db.SerializedUnits.AddRange(
                new SerializedUnit { ProductId = 1, SerialNumber = "CAB-SN", CurrentBranchId = 3 },
                new SerializedUnit { ProductId = 1, SerialNumber = "SOLD-SN", CurrentBranchId = 2, Status = "Sold" });
            db.InventoryMovements.AddRange(
                new InventoryMovement { Type = MovementType.Receipt, DestinationBranchId = 2,
                    Items = [new InventoryMovementItem { ProductId = 2, Quantity = 7 }] },
                new InventoryMovement { Type = MovementType.Transfer, SourceBranchId = 2, DestinationBranchId = 3,
                    Items = [new InventoryMovementItem { ProductId = 2, Quantity = 2 }] });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/inventory/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/inventory/stock?branchId=999")).StatusCode);
        foreach (var term in new[] { "notebook", "NOTEBOOK", "7790000000011", "nb-10001" })
        {
            var rows = await client.GetFromJsonAsync<JsonElement>("/api/inventory/stock?branchId=2&search=" + term);
            Assert.Equal(1, rows.GetArrayLength());
            Assert.Equal(1, rows[0].GetProperty("stock").GetInt32());
        }
        var cables = await client.GetFromJsonAsync<JsonElement>("/api/inventory/stock?branchId=2&search=CABLE");
        Assert.Equal(5, cables[0].GetProperty("stock").GetInt32());
        var other = await client.GetFromJsonAsync<JsonElement>("/api/inventory/stock?branchId=3&search=cable");
        Assert.Equal(2, other[0].GetProperty("stock").GetInt32());
        var serialOtherBranch = await client.GetFromJsonAsync<JsonElement>("/api/inventory/stock?branchId=2&search=cab-sn");
        Assert.Equal(0, serialOtherBranch.GetArrayLength());
    }

    [Theory]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.Administrator, false)]
    public async Task StaleAdminCookieCannotCreateBranches(UserRole role, bool active)
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
            var user = await db.Users.SingleAsync();
            user.Role = role;
            user.IsActive = active;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/branches",
            new { code = "NO", name = "No permitida" })).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<GamexidDbContext>().Branches.CountAsync());
    }

    [Fact]
    public async Task ActiveAdminCanCreateBranch()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/branches",
            new { code = "CAB", name = "Caballito" })).StatusCode);
    }

    [Theory]
    [InlineData("USD", "1,234.56", "USD", 1234.56)]
    [InlineData("US$", "1.234,56", "USD", 1234.56)]
    [InlineData("U$S", "50,00", "USD", 50)]
    [InlineData("ARS", "1.234,56", "ARS", 1234.56)]
    public void DetectsCurrencyAndUnitPrice(string marker, string amount, string currency, decimal price)
    {
        var line = PdfInvoiceExtractor.FromLabels($"EAN 7790000000011 Producto Notebook Cantidad 2 Precio unitario {marker} {amount} Total {marker} 9999")!;
        Assert.Equal(currency, line.Currency);
        Assert.Equal(price, line.UnitPrice);
        Assert.Contains(line.Warnings, w => w.Contains("no coincide"));
    }

    [Fact]
    public void ReferenceUsesNumberBelowBranchOrderAndCurrencyUsesExplicitLabel()
    {
        var extraction = new PdfInvoiceExtractor().Extract(PdfAndConfigurationTests.Pdf(
            "Factura 99999", "PEDIDO SUCURSAL", "00012345", "Moneda: ARS",
            "EAN 7790000000011 Producto Notebook Cantidad 2 Precio $ 100,00 Total $ 200,00"));
        Assert.Equal("00012345", extraction.Reference);
        Assert.Equal("ARS", Assert.Single(extraction.Lines).Currency);
        Assert.Null(new PdfInvoiceExtractor().Extract(PdfAndConfigurationTests.Pdf("Factura 99999")).Reference);
        Assert.Null(PdfInvoiceExtractor.Currency("USD 100 ARS 150000"));
    }
}
