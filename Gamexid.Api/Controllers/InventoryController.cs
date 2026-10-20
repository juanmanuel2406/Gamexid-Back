using System.Security.Claims;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/inventory")]
public class InventoryController(GamexidDbContext db) : ControllerBase
{
    [HttpGet("stock")]
    public async Task<IActionResult> Stock([FromQuery] int branchId, CancellationToken ct, [FromQuery] string? search = null)
    {
        if (branchId <= 0 || !await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, ct))
            return BadRequest(new { mensaje = "Seleccioná una sucursal activa." });
        var term = search?.Trim().ToUpperInvariant() ?? "";
        if (term.Length > 100 || term.Any(char.IsControl))
            return BadRequest(new { mensaje = "La búsqueda admite hasta 100 caracteres." });
        var products = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (term.Length > 0)
            products = products.Where(p => p.Name.ToUpper().Contains(term) || p.Ean.ToUpper().Contains(term) ||
                p.Sku.ToUpper().Contains(term) || db.SerializedUnits.Any(u => u.ProductId == p.Id &&
                    u.CurrentBranchId == branchId && u.SerialNumber.ToUpper().Contains(term)));
        var rows = await products.OrderBy(p => p.Name).Select(p => new {
            p.Id, p.Name, p.Sku, p.Ean, p.RequiresSerialNumber, p.IsActive, BranchId = branchId,
            Stock = p.RequiresSerialNumber
                ? db.SerializedUnits.Count(u => u.ProductId == p.Id && u.CurrentBranchId == branchId && u.Status == "Available")
                : (from item in db.InventoryMovementItems
                   join movement in db.InventoryMovements on item.InventoryMovementId equals movement.Id
                   where item.ProductId == p.Id && item.SerializedUnitId == null &&
                       (movement.DestinationBranchId == branchId || movement.SourceBranchId == branchId)
                   select (movement.DestinationBranchId == branchId ? item.Quantity : 0) -
                       (movement.SourceBranchId == branchId ? item.Quantity : 0)).Sum()
        }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("movements")]
    public async Task<IActionResult> Movements(CancellationToken ct)
    {
        var list = await db.InventoryMovements.AsNoTracking().Include(x => x.Items).OrderByDescending(x => x.Id).ToListAsync(ct);
        return Ok(list.Select(View));
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> Receipt(ReceiptRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct)) return Unauthorized();
        if (!await db.Branches.AnyAsync(b => b.Id == request.DestinationBranchId && b.IsActive && b.Code == "DEP-CENTRAL", ct))
            return BadRequest(new { mensaje = "El destino del ingreso debe ser Morón (Depósito Central)." });
        if (request.Items is null || request.Items.Length is < 1 or > 100 || request.Items.Any(i => i is null))
            return BadRequest(new { mensaje = "Agregá entre 1 y 100 productos." });
        var ids = request.Items.Select(i => i.ProductId).Distinct().ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var requestedSerials = request.Items.SelectMany(i => i.Serials ?? []).Where(s => s != null).Select(s => s.Trim().ToUpperInvariant()).ToArray();
        if (requestedSerials.Length > 10000) return BadRequest(new { mensaje = "El ingreso admite hasta 10000 seriales." });
        var existingSerials = await db.SerializedUnits.Where(u => requestedSerials.Contains(u.SerialNumber.ToUpper())).Select(u => u.SerialNumber).ToListAsync(ct);
        var validation = ReceiptValidation.Validate(request, products, existingSerials);
        if (validation != null) return Conflict(new { mensaje = validation });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var movement = new InventoryMovement {
            Type = MovementType.Receipt, DestinationBranchId = request.DestinationBranchId,
            RegisteredByUserId = userId, Notes = request.Notes?.Trim()
        };
        var newUnits = request.Items.SelectMany(i => i.Serials.Select(serial => new SerializedUnit {
            ProductId = i.ProductId, SerialNumber = serial.Trim().ToUpperInvariant(), CurrentBranchId = request.DestinationBranchId
        })).ToList();
        db.SerializedUnits.AddRange(newUnits);
        try
        {
            await db.SaveChangesAsync(ct);
            foreach (var item in request.Items)
                if (item.RequiresSerialNumber)
                    foreach (var unit in newUnits.Where(u => u.ProductId == item.ProductId))
                        movement.Items.Add(new InventoryMovementItem { ProductId = item.ProductId, SerializedUnitId = unit.Id, Quantity = 1 });
                else movement.Items.Add(new InventoryMovementItem { ProductId = item.ProductId, Quantity = item.Quantity });
            db.InventoryMovements.Add(movement);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            return Conflict(new { mensaje = "No se registró el ingreso. Revisá si algún serial acaba de ingresarse desde otro dispositivo." });
        }
        return Ok(View(movement));
    }

    private static object View(InventoryMovement m) => new {
        m.Id, type = m.Type switch { MovementType.Receipt => "Ingreso", MovementType.Transfer => "Transferencia", MovementType.Sale => "Venta", MovementType.Return => "Devolución", _ => "Ajuste" },
        m.SourceBranchId, m.DestinationBranchId, m.RegisteredByUserId, m.CreatedAtUtc, m.Notes, m.Items
    };
}
