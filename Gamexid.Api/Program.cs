using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Gamexid.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets(DatabaseConfiguration.SecretsId);
builder.Configuration.AddEnvironmentVariables();
var database = DatabaseConfiguration.Load(builder.Configuration);
builder.Services.AddSingleton(database);
builder.Services.AddDbContext<GamexidDbContext>(database.Configure);
builder.Services.AddSingleton<PdfInvoiceExtractor>();
builder.Services.AddCors(options => options.AddPolicy("Gamexid", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var protection = builder.Services.AddDataProtection().SetApplicationName("Gamexid.Access");
if (builder.Configuration["Access:KeysPath"] is { Length: > 0 } keys)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "Gamexid.Dev" : "__Host-Gamexid";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("login", opt => { opt.PermitLimit = 10; opt.Window = TimeSpan.FromMinutes(1); opt.QueueLimit = 0; });
    options.AddConcurrencyLimiter("pdf", opt => { opt.PermitLimit = 2; opt.QueueLimit = 0; });
    options.AddFixedWindowLimiter("gc", opt => { opt.PermitLimit = 20; opt.Window = TimeSpan.FromMinutes(1); opt.QueueLimit = 0; });
});
builder.Services.AddHttpClient<GamingCityApi>(client =>
{
    client.BaseAddress = new Uri("https://portal.gaming-city.com.ar/gcapi/");
    client.Timeout = TimeSpan.FromSeconds(20);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
var app = builder.Build();
if (args.Contains("--check-gc"))
{
    try {
        var gc = app.Services.GetRequiredService<GamingCityApi>();
        var catalog = await gc.Query("products", "RYZEN 5 3400G", CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        var product = await gc.ById("4715", CancellationToken.None);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { connected = true, catalog.Total, product.Id }));
    } catch {
        Console.WriteLine("No se pudo verificar Gaming City; credencial y respuesta omitidas.");
        Environment.ExitCode = 2;
    }
    return;
}
database = app.Services.GetRequiredService<DatabaseConfiguration>();
if (args.Contains("--check-db"))
{
    Environment.ExitCode = await DatabaseDiagnostics.CheckAsync(database);
    return;
}
if (args.Contains("--migrate"))
{
    if (database.ConnectionString is null) throw new InvalidOperationException("Configurá primero la conexión privada de Aiven y el certificado CA.");
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GamexidDbContext>();
    await db.Database.MigrateAsync();
    await BranchCatalog.SeedAsync(db);
    return;
}
if (args.Contains("--create-admin"))
{
    using var scope = app.Services.CreateScope();
    await AdminProvisioning.CreateAsync(scope.ServiceProvider.GetRequiredService<GamexidDbContext>());
    return;
}
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.UseCors("Gamexid");
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE" && context.Request.Headers["X-Gamexid"] != "1")
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { mensaje = "Falta la cabecera de seguridad de Gamexid." });
        return;
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var requiresDatabase = path.StartsWith("/api/products") || path.StartsWith("/api/branches") ||
        path.StartsWith("/api/inventory") || path.StartsWith("/api/orders") ||
        path is "/api/access/login" or "/api/auth/login" or "/api/access/me";
    if (requiresDatabase && database.ConnectionString is null)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { mensaje = "Base de datos sin configurar. Completá la conexión privada de Aiven y el certificado CA.", code = database.Status });
        return;
    }
    try { await next(); }
    catch (Exception e) when (e is MySqlConnector.MySqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { mensaje = "No se pudo completar la operación en MySQL. Revisá conexión, certificado y migraciones." });
    }
});
app.MapControllers();
GamingCityApi.Map(app);
app.Run();
public partial class Program { }
