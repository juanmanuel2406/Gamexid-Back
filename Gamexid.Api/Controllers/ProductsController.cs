using System.ComponentModel.DataAnnotations;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

public record CreateProductRequest(
    [Required, MaxLength(40)] string Sku,
    [Required, MaxLength(180)] string Name,
    [Required, RegularExpression(@"^(?:\d{8}|\d{12,14})$")] string Ean,
    bool RequiresSerialNumber);

[ApiController, Route("api/products")]
public class ProductsController(GamexidDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await db.Products.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));

    [HttpGet("ean/{ean}")]
    public async Task<IActionResult> GetByEan(string ean, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Ean == ean.Trim() && x.IsActive, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string code, CancellationToken ct)
    {
        var value = code.Trim();
        if (value.Length is < 1 or > 100 || value.Any(char.IsControl))
            return BadRequest(new { mensaje = "Ingresá un EAN, SKU o serial de hasta 100 caracteres." });
        var normalized = value.ToUpperInvariant();
        var unit = await db.SerializedUnits.AsNoTracking().SingleOrDefaultAsync(x => x.SerialNumber.ToUpper() == normalized, ct);
        var candidates = await db.Products.AsNoTracking().Where(p => p.IsActive &&
            (p.Ean == value || p.Sku.ToUpper() == normalized || (unit != null && p.Id == unit.ProductId))).ToListAsync(ct);
        if (candidates.Count > 1)
            return Conflict(new { mensaje = "El código coincide con más de un producto. Identificá el producto por su EAN.", code = "ambiguous_code" });
        if (candidates.Count == 0)
            return NotFound(new { mensaje = "Código sin relación registrada. Un serial nuevo necesita asociarse primero a un producto por EAN o SKU.", code = "unknown_code" });
        var product = candidates[0];
        return Ok(new { product, matchedBy = unit != null ? "serial" : product.Ean == value ? "ean" : "sku", unit, alreadyInInventory = unit != null });
    }

    [HttpGet("{id:int}/units")]
    public async Task<IActionResult> Units(int id, CancellationToken ct) =>
        Ok(await db.SerializedUnits.AsNoTracking().Where(x => x.ProductId == id).OrderBy(x => x.SerialNumber).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var sku = request.Sku.Trim();
        var name = request.Name.Trim();
        if (sku.Length == 0 || name.Length == 0 || sku.Any(char.IsControl))
            return BadRequest(new { mensaje = "Completá nombre y SKU válidos." });
        if (await db.Products.AnyAsync(x => x.Sku.ToUpper() == sku.ToUpper() || x.Ean == request.Ean, ct))
            return Conflict(new { mensaje = "El SKU o EAN ya está registrado." });
        var product = new Product { Sku = sku, Name = name, Ean = request.Ean, RequiresSerialNumber = request.RequiresSerialNumber };
        db.Products.Add(product);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { mensaje = "No se pudo crear: comprobá si el SKU o EAN ya fue registrado." }); }
        return Created($"/api/products/ean/{product.Ean}", product);
    }
}
