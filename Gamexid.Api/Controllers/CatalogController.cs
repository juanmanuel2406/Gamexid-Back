using System.Security.Claims;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/catalog"), Authorize, EnableRateLimiting("gc")]
public class CatalogController(GamexidDbContext db, GamingCityApi catalog) : ControllerBase
{
    [HttpGet("products")]
    public Task<IActionResult> Products(int branchId, CancellationToken ct, string? search = null,
        bool onlyInStock = true, bool hideCombos = true, int page = 1, int pageSize = 50) =>
        Read(branchId, portal => catalog.Query("products", search, ct, portal.Id, onlyInStock, hideCombos, page, pageSize, allowEmpty: true), ct);

    [HttpGet("products/{id}")]
    public Task<IActionResult> Product(string id, int branchId, CancellationToken ct) =>
        Read(branchId, portal => catalog.ById(id, ct, portal.Id), ct);

    private async Task<IActionResult> Read<T>(int branchId, Func<GamingCityBranch, Task<T>> read, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        if (user == null) return Unauthorized();
        if (branchId <= 0) return BadRequest(new { mensaje = "Seleccioná una sucursal." });
        if (user.Role != UserRole.Administrator && user.BranchId != branchId) return Forbid();
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == branchId && b.IsActive, ct);
        if (branch == null) return BadRequest(new { mensaje = "La sucursal no está activa." });
        try { return Ok(new { data = await read(catalog.ForGamexidBranch(branch.Code)), branchId = branch.Id, branchName = branch.Name }); }
        catch (ArgumentException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(new { mensaje = "Producto no disponible en el catálogo." }); }
        catch (InvalidOperationException) { return StatusCode(503, new { mensaje = "Esta sucursal no tiene configurada su conexión al portal." }); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests) {
            Response.Headers.RetryAfter = "60";
            return StatusCode(429, new { mensaje = "El portal alcanzó su límite de consultas. Esperá un minuto y volvé a buscar." });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested) {
            return StatusCode(502, new { mensaje = "No se pudo consultar el catálogo de esta sucursal. Reintentá." });
        }
    }
}
