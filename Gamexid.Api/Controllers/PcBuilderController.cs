using System.Security.Claims;
using Gamexid.Api.PcBuilder;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/pc-builder"), Authorize, EnableRateLimiting("pc-builder")]
public class PcBuilderController(PcBuilderService builder, GamexidDbContext db, GamingCityApi catalog) : ControllerBase
{
    [HttpGet("prices/{id}")]
    public Task<IActionResult> Prices(string id, CancellationToken ct, [FromQuery] int? branchId = null) =>
        Run(branchId, (user, branch, portal) => builder.Prices(user, id, portal.Id, ct), ct);
    [HttpGet("mercadolibre/{id}")]
    public Task<IActionResult> MercadoLibre(string id, CancellationToken ct, [FromQuery] int? branchId = null) =>
        Run(branchId, (user, branch, portal) => builder.MercadoLibre(user, id, portal.Id, ct), ct);
    public record ConfirmRequest(string ProductId, string OfferId, int? BranchId = null);
    [HttpPost("confirm"), RequestSizeLimit(2000)]
    public Task<IActionResult> Confirm(ConfirmRequest request, CancellationToken ct) =>
        Run(request.BranchId, (user, branch, portal) => builder.ConfirmListing(user, request.ProductId, request.OfferId, portal.Id, ct), ct);
    [HttpPost("quote"), RequestSizeLimit(12000)]
    public Task<IActionResult> Quote(PcRequest request, CancellationToken ct) =>
        Run(request.BranchId, async (user, branch, portal) => (await builder.Quote(user, request.Items, portal.Id, ct)) with { BranchId = branch.Id }, ct);
    [HttpPost("options"), RequestSizeLimit(12000)]
    public Task<IActionResult> Options(PcStepRequest request, CancellationToken ct) =>
        Run(request.BranchId, async (user, branch, portal) => (await builder.Options(request, portal.Id, branch.Name, ct)) with { BranchId = branch.Id }, ct);

    private async Task<IActionResult> Run<T>(int? branchId, Func<int, Branch, GamingCityBranch, Task<T>> action, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        if (user == null) return Unauthorized();
        var selected = branchId ?? user.BranchId;
        if (selected is null or <= 0) return BadRequest(new { mensaje = "Seleccioná una sucursal de Gamexid." });
        if (user.Role != UserRole.Administrator && user.BranchId != selected) return Forbid();
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == selected && b.IsActive, ct);
        if (branch == null) return BadRequest(new { mensaje = "Seleccioná una sucursal activa de Gamexid." });
        try { return Ok(await action(user.Id, branch, catalog.ForGamexidBranch(branch.Code))); }
        catch (ArgumentException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(new { mensaje = "El producto ya no está disponible en el catálogo." }); }
        catch (InvalidOperationException) { return StatusCode(503, new { mensaje = "La sucursal no tiene una conexión válida con el portal. Revisá su configuración." }); }
        catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !ct.IsCancellationRequested) {
            return StatusCode(502, new { mensaje = "No se pudo verificar la fuente. Esperá un momento y reintentá." });
        }
    }
}
