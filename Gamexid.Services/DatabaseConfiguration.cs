using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Gamexid.Services;

public sealed record DatabaseConfiguration(string? ConnectionString, string Status, Version ServerVersion)
{
    public const string SecretsId = "gamexid-local-development";

    public static DatabaseConfiguration Load(IConfiguration config)
    {
        var version = Version.TryParse(config["Database:ServerVersion"], out var parsed) ? parsed : new Version(8, 0, 36);
        try
        {
            var raw = config.GetConnectionString("GamexidDatabase");
            if (string.IsNullOrWhiteSpace(raw)) raw = config["MYSQL_URL"];
            var file = config["Database:ConnectionFile"];
            if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(file))
                raw = File.ReadAllText(file).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return new(null, "not_configured", version);
            var cs = Parse(raw);
            if (string.IsNullOrWhiteSpace(cs.Server) || string.IsNullOrWhiteSpace(cs.Database) ||
                string.IsNullOrWhiteSpace(cs.UserID) || string.IsNullOrWhiteSpace(cs.Password))
                return new(null, "invalid_configuration", version);
            var ca = config["Database:CaCertificate"];
            if (!string.IsNullOrWhiteSpace(ca)) cs.SslCa = Path.GetFullPath(ca);
            cs.SslMode = MySqlSslMode.VerifyFull;
            if (!string.IsNullOrWhiteSpace(cs.SslCa) && !File.Exists(cs.SslCa))
                return new(null, "missing_ca_certificate", version);
            cs.ConnectionTimeout = 10;
            cs.DefaultCommandTimeout = 20;
            cs.AllowUserVariables = false;
            return new(cs.ConnectionString, "configured", version);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or FormatException)
        {
            return new(null, "invalid_configuration", version);
        }
    }

    public static MySqlConnectionStringBuilder Parse(string raw)
    {
        if (!raw.TrimStart().StartsWith("mysql://", StringComparison.OrdinalIgnoreCase))
            return new MySqlConnectionStringBuilder(raw);
        var uri = new Uri(raw.Trim());
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2) throw new FormatException("URI MySQL incompleta.");
        return new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 3306u : (uint)uri.Port,
            UserID = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
        };
    }

    public void Configure(DbContextOptionsBuilder options)
    {
        if (ConnectionString is null)
            throw new InvalidOperationException("La conexión de Gamexid no está configurada. Revisá Database:ConnectionFile y Database:CaCertificate o ConnectionStrings:GamexidDatabase.");
        options.UseMySql(ConnectionString, new MySqlServerVersion(ServerVersion));
    }
}
