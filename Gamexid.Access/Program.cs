using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;

// Provision the single pilot administrator without storing a plaintext password.
if (args.Contains("--provision")) {
    var path = args.SkipWhile(a => a != "--provision").Skip(1).First();
    if (File.Exists(path)) throw new InvalidOperationException("The credential file already exists.");
    var password = Console.ReadLine() ?? throw new InvalidOperationException("Password required on stdin.");
    if (password.Length < 8) throw new InvalidOperationException("At least eight characters required.");
    File.WriteAllText(path, new PasswordHasher<string>().HashPassword("admin@gamexid.com", password));
    return;
}

var builder = WebApplication.CreateBuilder(args);
var credentialPath = builder.Configuration["Access:PasswordHashFile"] ?? throw new InvalidOperationException("Access__PasswordHashFile required.");
var hash = File.ReadAllText(credentialPath).Trim();
var email = builder.Configuration["Access:Email"] ?? "admin@gamexid.com";
var hasher = new PasswordHasher<string>();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(
    builder.Configuration["Access:KeysPath"] ?? "/var/lib/gamexid-access/keys")).SetApplicationName("Gamexid.Access");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name = "__Host-Gamexid";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        "pilot-login", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) => {
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Method == "POST" && context.Request.Headers["X-Gamexid"] != "1") {
        context.Response.StatusCode = 403; return;
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
object Profile() => new { id = 1, fullName = "Juan Manuel", email, role = "Administrator", isActive = true };
app.MapGet("/api/access/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/access/me", () => Results.Ok(Profile())).RequireAuthorization();
app.MapPost("/api/access/login", async (LoginRequest request, HttpContext context) => {
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
        return Results.Json(new { mensaje = "Credenciales inválidas." }, statusCode: 401);
    var valid = hasher.VerifyHashedPassword(email, hash, request.Password) != PasswordVerificationResult.Failed;
    if (!valid || !string.Equals(request.Email.Trim(), email, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { mensaje = "Credenciales inválidas." }, statusCode: 401);
    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, email),
        new Claim(ClaimTypes.Role, "Administrator")
    }, CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    return Results.Ok(Profile());
}).RequireRateLimiting("login");
app.MapPost("/api/access/logout", async (HttpContext context) => {
    await context.SignOutAsync(); return Results.NoContent();
});
app.Run();
record LoginRequest(string Email, string Password);
