using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Gamexid.Models;
using Gamexid.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gamexid.Api.Controllers;

public record LoginRequest([Required, EmailAddress, MaxLength(160)] string Email, [Required, MaxLength(256)] string Password);

[ApiController]
public class AuthController(GamexidDbContext db) : ControllerBase
{
    [AllowAnonymous, HttpPost("api/access/login"), HttpPost("api/auth/login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
        var valid = false;
        if (user is { IsActive: true } && !string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            try
            {
                valid = user.PasswordHash.StartsWith("$2")
                    ? BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)
                    : new PasswordHasher<string>().VerifyHashedPassword(user.Email, user.PasswordHash, request.Password) != PasswordVerificationResult.Failed;
            }
            catch (Exception e) when (e is FormatException or ArgumentException) { valid = false; }
        }
        if (!valid || user is null) return Unauthorized(new { mensaje = "Credenciales inválidas." });
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Ok(Profile(user));
    }

    [HttpGet("api/access/me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        return user is null ? Unauthorized() : Ok(Profile(user));
    }

    [HttpPost("api/access/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    private static object Profile(User user) => new { user.Id, user.FullName, user.Email, user.Role, user.BranchId, user.IsActive };
}
