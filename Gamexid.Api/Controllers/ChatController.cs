using System.Security.Claims;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

[ApiController, Route("api/chat"), Authorize]
public class ChatController(GamexidDbContext db, IGamexidAiService ai) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (await Actor(ct) == null) return Unauthorized();
        return Ok(new { configured = ai.IsConfigured, provider = "Gemini" });
    }

    [HttpPost("ask"), EnableRateLimiting("chat"), RequestSizeLimit(16000)]
    public async Task<IActionResult> Ask(AiQuestion question, CancellationToken ct)
    {
        var actor = await Actor(ct);
        if (actor == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(question.Mensaje) || question.Mensaje.Length > 1500 ||
            question.ContextoPantalla is not ("Productos" or "Pedidos") || question.SucursalId <= 0 ||
            question.PreguntasAnteriores is { Length: > 6 } ||
            question.PreguntasAnteriores?.Any(text => text == null || text.Length > 1500) == true)
            return BadRequest(new { mensaje = "Revisá el mensaje, la pantalla y la sucursal seleccionada." });
        if (actor.Role != UserRole.Administrator && actor.BranchId != question.SucursalId) return Forbid();
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == question.SucursalId && b.IsActive, ct);
        if (branch == null) return BadRequest(new { mensaje = "Seleccioná una sucursal activa." });
        if (!ai.IsConfigured) return StatusCode(503, new { mensaje = "El asistente todavía no está activado. Falta configurar Gemini en el servidor." });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(65));
        try
        {
            return Ok(await ai.AskAsync(question, new(actor.Id, branch.Id, branch.Name), deadline.Token));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return StatusCode(429, new { mensaje = "Se alcanzó el límite de consultas del asistente. Esperá un momento y reintentá." });
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or InvalidOperationException)
        {
            return StatusCode(502, new { mensaje = "No pude completar la consulta. Reintentá en un momento; no se modificó ningún dato." });
        }
    }

    private async Task<User?> Actor(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
    }
}
