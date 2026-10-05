using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gamexid.Tests;

public sealed class AivenFactAttribute : FactAttribute
{
    public AivenFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GAMEXID_TEST_AIVEN") != "1")
            Skip = "Prueba real de Aiven: requiere autorización explícita con GAMEXID_TEST_AIVEN=1.";
    }
}

public sealed class AivenFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}

public class AivenIntegrationTests
{
    [AivenFact]
    public async Task RealMySqlReceiptLookupPdfAndCleanup()
    {
        using var factory = new AivenFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Gamexid", "1");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        Assert.True(db.Database.IsMySql());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        var marker = "GXQA-" + Guid.NewGuid().ToString("N")[..16];
        var user = new User { Email = marker.ToLowerInvariant() + "@example.invalid", FullName = marker, Role = UserRole.Administrator, PasswordHash = BCrypt.Net.BCrypt.HashPassword(marker) };
        var ean = Random.Shared.NextInt64(1000000000000, 9999999999999).ToString();
        var productId = 0;
        try
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/access/login", new { email = user.Email, password = marker })).StatusCode);
            var branches = await client.GetFromJsonAsync<JsonElement>("/api/branches");
            Assert.Equal(16, branches.GetArrayLength());
            var depotId = branches.EnumerateArray().Single(b => b.GetProperty("code").GetString() == "DEP-CENTRAL").GetProperty("id").GetInt32();
            Assert.Equal(2, depotId);
            var create = await client.PostAsJsonAsync("/api/products", new { sku = marker, name = marker, ean, requiresSerialNumber = true });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            productId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            foreach (var code in new[] { ean, marker.ToLowerInvariant() })
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products/lookup?code=" + code)).StatusCode);
            var receipt = new ReceiptRequest(depotId, marker, [new ReceiptLine(productId, 1, [marker + "-SN"], true)]);
            var saved = await client.PostAsJsonAsync("/api/inventory/receipts", receipt);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var movement = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(movement.GetProperty("items")[0].GetProperty("serializedUnitId").GetInt32() > 0);
            var identified = await client.GetFromJsonAsync<JsonElement>("/api/products/lookup?code=" + marker + "-SN");
            Assert.Equal("serial", identified.GetProperty("matchedBy").GetString());
            Assert.True(identified.GetProperty("alreadyInInventory").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/inventory/receipts", receipt)).StatusCode);
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(PdfAndConfigurationTests.Pdf("EAN " + ean + " Producto QA Cantidad 2 Precio ARS 100,00 Total 200,00")), "file", "qa.pdf");
            var pdf = await client.PostAsync("/api/documents/extract", form);
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            var extraction = await pdf.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(100m, extraction.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal());
        }
        finally
        {
            await using var cleanup = await db.Database.BeginTransactionAsync();
            var movements = db.InventoryMovements.Where(m => m.RegisteredByUserId == user.Id && m.Notes == marker);
            var ids = await movements.Select(m => m.Id).ToListAsync();
            await db.InventoryMovementItems.Where(i => ids.Contains(i.InventoryMovementId)).ExecuteDeleteAsync();
            await movements.ExecuteDeleteAsync();
            var productIds = await db.Products.Where(p => p.Sku == marker && p.Name == marker).Select(p => p.Id).ToListAsync();
            await db.SerializedUnits.Where(u => productIds.Contains(u.ProductId) && u.SerialNumber == marker + "-SN").ExecuteDeleteAsync();
            await db.Products.Where(p => productIds.Contains(p.Id) && p.Sku == marker).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == user.Id && u.Email == user.Email).ExecuteDeleteAsync();
            await cleanup.CommitAsync();
            Assert.False(await db.Products.AnyAsync(p => p.Sku == marker));
            Assert.False(await db.Users.AnyAsync(u => u.Email == user.Email));
        }
    }
}
