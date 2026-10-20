using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

public record CreateBranchRequest([Required, MaxLength(20)] string Code, [Required, MaxLength(120)] string Name, [MaxLength(200)] string? Address);
[ApiController, Route("api/branches")]
public class BranchesController(GamexidDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await db.Branches.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));
    [HttpPost, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Create(CreateBranchRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role == UserRole.Administrator, ct))
            return Forbid();
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { mensaje = "Completá código y nombre." });
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Branches.AnyAsync(x => x.Code == code, ct)) return Conflict(new { mensaje = "Ya existe una sucursal con ese código." });
        var branch = new Branch { Code = code, Name = request.Name.Trim(), Address = request.Address?.Trim() };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);
        return Created($"/api/branches/{branch.Id}", branch);
    }
}
