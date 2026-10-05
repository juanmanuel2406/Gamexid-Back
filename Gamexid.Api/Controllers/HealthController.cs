using Gamexid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

[ApiController]
public class HealthController(DatabaseConfiguration database, IServiceProvider services) : ControllerBase
{
    [AllowAnonymous, HttpGet("api/health/live")]
    public IActionResult Live() => Ok(new { status = "ok", service = "Gamexid API" });

    [AllowAnonymous, HttpGet("api/health"), HttpGet("api/access/health")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (database.ConnectionString is null)
            return StatusCode(503, new { status = "not_ready", database = database.Status, service = "Gamexid API" });
        try
        {
            var db = services.GetRequiredService<GamexidDbContext>();
            if (!await db.Database.CanConnectAsync(ct))
                return StatusCode(503, new { status = "not_ready", database = "unavailable" });
            if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
                return StatusCode(503, new { status = "not_ready", database = "migrations_pending" });
            return Ok(new { status = "ok", database = "connected", storage = "mysql", service = "Gamexid API" });
        }
        catch (Exception e) when (e is MySqlConnector.MySqlException or InvalidOperationException)
        {
            return StatusCode(503, new { status = "not_ready", database = "unavailable" });
        }
    }

    [HttpGet("api/access/integrations")]
    public IActionResult Integrations() => Ok(new { pdfPig = true, gemini = false, database = database.Status, storage = "mysql" });
}
