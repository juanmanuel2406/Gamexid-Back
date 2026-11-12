using System.Security.Claims;
using Gamexid.Api.PcBuilder;
using Gamexid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/pc-builder"), Authorize, EnableRateLimiting("pc-builder")]
public class PcBuilderController(PcBuilderService builder, GamexidDbContext db) : ControllerBase
{
    [HttpGet("prices/{id}")]
    public Task<IActionResult> Prices(string id, CancellationToken ct) => Run(user => builder.Prices(user, id, ct), ct);
    [HttpGet("mercadolibre/{id}")]
    public Task<IActionResult> MercadoLibre(string id, CancellationToken ct) => Run(user => builder.MercadoLibre(user, id, ct), ct);
    public record ConfirmRequest(string ProductId, string OfferId);
    [HttpPost("confirm"), RequestSizeLimit(2000)]
    public Task<IActionResult> Confirm(ConfirmRequest request, CancellationToken ct) => Run(user => builder.ConfirmListing(user, request.ProductId, request.OfferId, ct), ct);
    [HttpPost("quote"), RequestSizeLimit(12000)]
    public Task<IActionResult> Quote(PcRequest request, CancellationToken ct) => Run(user => builder.Quote(user, request.Items, ct), ct);
    private async Task<IActionResult> Run<T>(Func<int, Task<T>> action, CancellationToken ct) {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) ||
            !await db.Users.AnyAsync(u => u.Id == user && u.IsActive, ct)) return Unauthorized();
        try { return Ok(await action(user)); }
        catch (ArgumentException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(new { mensaje = "El producto ya no está disponible en el catálogo." }); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException || ex is OperationCanceledException && !ct.IsCancellationRequested) {
            return StatusCode(502, new { mensaje = "No se pudo verificar la fuente. Esperá un momento y reintentá." });
        }
    }
}
