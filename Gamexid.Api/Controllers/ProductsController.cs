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
public class ProductsController : ControllerBase
{
    private readonly GamexidDbContext db;

    public ProductsController(GamexidDbContext db)
    {
        this.db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var productos = await db.Products.AsNoTracking()
            .OrderBy(producto => producto.Name)
            .ToListAsync(ct);
        return Ok(productos);
    }
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
        {
            var products = await db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
            var serials = await db.SerializedUnits.AsNoTracking().Select(u => new { u.ProductId, u.SerialNumber }).ToListAsync(ct);
            var serialScores = serials.GroupBy(u => u.ProductId).ToDictionary(g => g.Key,
                g => g.Max(u => ProductMatching.Score(value, u.SerialNumber)));
            var suggestions = products.Select(producto => new
                {
                    product = producto,
                    score = CalcularCoincidencia(value, producto, serialScores.GetValueOrDefault(producto.Id))
                })
                .Where(x => x.score >= .75).OrderByDescending(x => x.score).ThenBy(x => x.product.Id).Take(5).ToArray();
            return NotFound(new { mensaje = "Código sin coincidencia exacta. Seleccioná un producto para confirmar la asociación.",
                code = "unknown_code", suggestions });
        }
        var product = candidates[0];
        var matchedBy = "sku";
        if (unit != null)
        {
            matchedBy = "serial";
        }
        else if (product.Ean == value)
        {
            matchedBy = "ean";
        }
        return Ok(new { product, matchedBy, unit, alreadyInInventory = unit != null });
    }

    private static double CalcularCoincidencia(string codigo, Product producto, double coincidenciaSerial)
    {
        var coincidenciaEan = ProductMatching.Score(codigo, producto.Ean);
        var coincidenciaSku = ProductMatching.Score(codigo, producto.Sku);
        var coincidenciaNombre = ProductMatching.Score(codigo, producto.Name);
        return Math.Max(Math.Max(coincidenciaEan, coincidenciaSku), Math.Max(coincidenciaNombre, coincidenciaSerial));
    }

    [HttpGet("{id:int}/units")]
    public async Task<IActionResult> Units(int id, CancellationToken ct, int? branchId = null)
    {
        var unidades = await db.SerializedUnits.AsNoTracking()
            .Where(unidad => unidad.ProductId == id && (branchId == null || unidad.CurrentBranchId == branchId))
            .OrderBy(unidad => unidad.SerialNumber)
            .ToListAsync(ct);
        return Ok(unidades);
    }
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { mensaje = "No se pudo crear: comprobá si el SKU o EAN ya fue registrado." });
        }
        return Created($"/api/products/ean/{product.Ean}", product);
    }
}
