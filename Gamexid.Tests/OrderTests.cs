using System.Net;
using System.Net.Http.Json;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gamexid.Tests;

public class OrderTests
{
    private static CreateOrderDto Draft(int expected = 2) => new(Guid.NewGuid(), "Supplier QA", "DOC-" + Guid.NewGuid(),
        2, DateTime.UtcNow, "invoice.pdf",
        [new("7790000000011", "NOTE-001", "Notebook", expected, 100m, 100m * expected, "ARS")]);

    [Fact]
    public async Task CreateListScanAndRetryPersistAtomically()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        var dto = Draft();
        var create = await client.PostAsJsonAsync("/api/orders", dto);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var order = (await create.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/orders", dto)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", dto with { Supplier = "Changed" })).StatusCode);
        var scan = new SaveSerialDto(order.Lines[0].Id, "order-sn-01");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials",
            scan with { Serial = order.Lines[0].Ean })).StatusCode);
        var first = await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials", scan);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Partial", (await first.Content.ReadFromJsonAsync<OrderResponseDto>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials", scan)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials",
            scan with { Serial = "NB-10001" })).StatusCode);
        var second = await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials", scan with { Serial = "ORDER-SN-02" });
        Assert.Equal("Received", (await second.Content.ReadFromJsonAsync<OrderResponseDto>())!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials",
            scan with { Serial = "OVER-LIMIT" })).StatusCode);
        var page = await client.GetFromJsonAsync<OrderPageDto>("/api/orders?branchId=2&status=Received&pageSize=1");
        Assert.Equal(1, page!.Total);
        Assert.Equal(2, Assert.Single(page.Items).Lines[0].Received);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(2, await db.SerialNumbers.CountAsync());
        Assert.Equal(2, await db.InventoryMovements.CountAsync());
        Assert.Equal(3, await db.SerializedUnits.CountAsync());
    }

    [Fact]
    public async Task ConflictRollsBackNewProductsAndHeader()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        var dto = Draft() with { Lines = [
            new("12345678", "NEW-PRODUCT", "New", 1, null, null, null),
            new("123456789012", "NOTE-001", "Conflict", 1, null, null, null)] };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/orders", dto)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(1, await db.Products.CountAsync());
    }

    [Fact]
    public async Task InvalidPayloadAndPaginationAreRejected()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", Draft(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", Draft() with { Supplier = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/orders?pageSize=1000")).StatusCode);
        var dto = Draft();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", dto with { Lines = [dto.Lines[0], dto.Lines[0]] })).StatusCode);
    }

    [Fact]
    public async Task AnonymousCannotReadOrWriteOrders()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/orders", Draft())).StatusCode);
    }

    [Fact]
    public async Task OperatorsCannotAccessAnotherBranch()
    {
        using var factory = new ApiFactory();
        using var client = await factory.Client();
        var response = await client.PostAsJsonAsync("/api/orders", Draft());
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
            var user = await db.Users.SingleAsync();
            user.Role = UserRole.Operator;
            user.BranchId = null;
            await db.SaveChangesAsync();
        }
        Assert.Empty((await client.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/orders", Draft())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/serials",
            new SaveSerialDto(order.Lines[0].Id, "DENIED"))).StatusCode);
    }
}
