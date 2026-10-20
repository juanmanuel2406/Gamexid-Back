using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/orders")]
public class OrdersController(GamexidDbContext db) : ControllerBase
{
    private Task<User?> Actor(CancellationToken ct) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct)
        : Task.FromResult<User?>(null);
    private static bool CanAccess(User actor, int branch) => actor.Role == UserRole.Administrator || actor.BranchId == branch;
    private IQueryable<Order> FullOrders() => db.Orders.Include(x => x.Lines).ThenInclude(x => x.Serials);
    private static bool Text(string? value, int max, bool required = true) =>
        value != null && (!required || !string.IsNullOrWhiteSpace(value)) && value.Length <= max && !value.Any(char.IsControl);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, int page = 1, int pageSize = 20,
        int? branchId = null, string? status = null, string? search = null)
    {
        var actor = await Actor(ct);
        if (actor is null) return Unauthorized();
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100 ||
            (status != null && status is not ("Pending" or "Partial" or "Received")) ||
            (search != null && !Text(search, 100, false))) return BadRequest(new { mensaje = "Filtros inválidos." });
        var query = FullOrders().AsNoTracking();
        if (actor.Role != UserRole.Administrator) query = query.Where(x => x.BranchId == actor.BranchId);
        if (branchId != null) query = query.Where(x => x.BranchId == branchId);
        if (status != null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Reference.Contains(search));
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Created).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new OrderPageDto(items.Select(View).ToArray(), total, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderDto dto, CancellationToken ct)
    {
        var actor = await Actor(ct);
        if (actor is null) return Unauthorized();
        if (!CanAccess(actor, dto.BranchId)) return Forbid();
        if (dto.Id == Guid.Empty || !Text(dto.Reference, 100) ||
            !Text(dto.FileName ?? "", 255, false) || dto.Created == default ||
            dto.Created > DateTime.UtcNow.AddDays(1) ||
            dto.Lines is null || dto.Lines.Length is < 1 or > 100 ||
            dto.Lines.Any(l => l == null || !Text(l.Name, 180) || !Text(l.Ean, 14) ||
                !Regex.IsMatch(l.Ean, @"^(?:\d{8}|\d{12,14})$") || !Text(l.Sku ?? "", 40, false) ||
                l.Expected is < 1 or > 10000 || l.UnitPrice is < 0 or > 9999999999999999.99m ||
                l.Total is < 0 or > 9999999999999999.99m ||
                (l.UnitPrice.HasValue && decimal.Round(l.UnitPrice.Value, 2) != l.UnitPrice) ||
                (l.Total.HasValue && decimal.Round(l.Total.Value, 2) != l.Total) ||
                l.Currency is not (null or "ARS" or "USD") ||
                ((l.UnitPrice.HasValue || l.Total.HasValue) && l.Currency == null)) ||
            dto.Lines.Select(l => l.Ean).Distinct().Count() != dto.Lines.Length)
            return BadRequest(new { mensaje = "Revisá documento, EAN, cantidades, moneda y precios (máximo dos decimales)." });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto))));
        var existing = await FullOrders().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.Id, ct);
        if (existing != null)
            return existing.CreatedByUserId == actor.Id && existing.RequestHash == hash
                ? Ok(View(existing)) : Conflict(new { mensaje = "El identificador ya pertenece a otro pedido. Recargá la lista." });
        if (!await db.Branches.AnyAsync(x => x.Id == dto.BranchId && x.IsActive, ct))
            return BadRequest(new { mensaje = "Sucursal inexistente o inactiva." });
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = new Order { Id = dto.Id, Reference = dto.Reference.Trim(),
                BranchId = dto.BranchId, Created = dto.Created.ToUniversalTime(), CreatedByUserId = actor.Id,
                FileName = dto.FileName ?? "", RequestHash = hash };
            foreach (var line in dto.Lines)
            {
                var sku = string.IsNullOrWhiteSpace(line.Sku) ? line.Ean : line.Sku.Trim().ToUpperInvariant();
                var product = await db.Products.FirstOrDefaultAsync(x => x.Ean == line.Ean, ct);
                if (product == null)
                {
                    if (await db.Products.AnyAsync(x => x.Sku == sku, ct))
                        return Conflict(new { mensaje = "El SKU ya corresponde a otro EAN." });
                    product = new Product { Ean = line.Ean, Sku = sku, Name = line.Name.Trim(), RequiresSerialNumber = true };
                    db.Products.Add(product);
                    await db.SaveChangesAsync(ct);
                }
                else if (!product.IsActive || (!string.IsNullOrWhiteSpace(line.Sku) && !product.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase)))
                    return Conflict(new { mensaje = "El EAN/SKU no coincide con un producto activo." });
                order.Lines.Add(new OrderItem { ProductId = product.Id, Ean = product.Ean, Sku = product.Sku,
                    Name = line.Name.Trim(), Expected = line.Expected, UnitPrice = line.UnitPrice, Total = line.Total, Currency = line.Currency });
            }
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return StatusCode(201, View(order));
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { mensaje = "Documento o producto duplicado. Recargá antes de reintentar; no se guardó parcialmente." });
        }
    }

    [HttpPost("{id:guid}/serials")]
    public async Task<IActionResult> SaveSerial(Guid id, SaveSerialDto dto, CancellationToken ct)
    {
        var actor = await Actor(ct);
        if (actor is null) return Unauthorized();
        if (!Text(dto.Serial, 100)) return BadRequest(new { mensaje = "Ingresá un serial de hasta 100 caracteres." });
        var serial = dto.Serial.Trim().ToUpperInvariant();
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await FullOrders().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (order == null) return NotFound();
            if (!CanAccess(actor, order.BranchId)) return Forbid();
            var line = order.Lines.FirstOrDefault(x => x.Id == dto.OrderItemId);
            if (line == null) return BadRequest(new { mensaje = "La línea no pertenece al pedido." });
            if (serial == line.Ean || serial.Equals(line.Sku, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { mensaje = "Se leyó el EAN/SKU en lugar del número de serie." });
            if (line.Serials.Any(x => x.Serial == serial)) return Ok(View(order));
            if (line.Serials.Count >= line.Expected) return Conflict(new { mensaje = "La cantidad pedida ya está completa." });
            if (await db.SerializedUnits.AnyAsync(x => x.SerialNumber.ToUpper() == serial, ct))
                return Conflict(new { mensaje = "El serial ya está registrado en inventario; no se duplicó la recepción." });
            if (!await db.Branches.AnyAsync(x => x.Id == order.BranchId && x.IsActive, ct) ||
                !await db.Products.AnyAsync(x => x.Id == line.ProductId && x.IsActive, ct))
                return Conflict(new { mensaje = "Sucursal o producto inactivo." });
            var unit = new SerializedUnit { ProductId = line.ProductId, SerialNumber = serial, CurrentBranchId = order.BranchId };
            db.SerializedUnits.Add(unit);
            await db.SaveChangesAsync(ct);
            line.Serials.Add(new SerialNumber { Serial = serial, SerializedUnitId = unit.Id });
            order.Status = order.Lines.All(x => x.Serials.Count == x.Expected) ? "Received" : "Partial";
            order.Version = Guid.NewGuid();
            db.InventoryMovements.Add(new InventoryMovement { Type = MovementType.Receipt,
                DestinationBranchId = order.BranchId, RegisteredByUserId = actor.Id, Notes = "Pedido " + order.Reference,
                Items = [new InventoryMovementItem { ProductId = line.ProductId, SerializedUnitId = unit.Id, Quantity = 1 }] });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(View(order));
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { mensaje = "Otro dispositivo actualizó el pedido o el serial. Recargá y reintentá." });
        }
    }

    private static OrderResponseDto View(Order o) => new(o.Id, o.Reference, o.BranchId,
        DateTime.SpecifyKind(o.Created, DateTimeKind.Utc), o.FileName, o.Status,
        o.Lines.OrderBy(x => x.Id).Select(l => new OrderLineResponseDto(l.Id, l.ProductId, l.Ean, l.Sku, l.Name,
            l.Expected, l.Serials.Count, l.UnitPrice, l.Total, l.Currency,
            l.Serials.OrderBy(s => s.Id).Select(s => new SerialResponseDto(s.Id, s.Serial, s.Status,
                DateTime.SpecifyKind(s.ScannedDate, DateTimeKind.Utc))).ToArray())).ToArray());
}
