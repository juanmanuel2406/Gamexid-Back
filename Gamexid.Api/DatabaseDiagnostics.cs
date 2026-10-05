using MySqlConnector;

namespace Gamexid.Services;

public static class DatabaseDiagnostics
{
    public static async Task<int> CheckAsync(DatabaseConfiguration configuration)
    {
        if (configuration.ConnectionString is null) { Console.WriteLine(configuration.Status); return 1; }
        try
        {
            await using var connection = new MySqlConnection(configuration.ConnectionString);
            await connection.OpenAsync();
            Console.WriteLine("Conexión verificada con TLS y validación del certificado.");
            await using var version = new MySqlCommand("SELECT VERSION()", connection);
            Console.WriteLine("MySQL: " + await version.ExecuteScalarAsync());
            await using var tables = new MySqlCommand("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME", connection);
            await using var reader = await tables.ExecuteReaderAsync();
            var count = 0;
            while (await reader.ReadAsync()) { count++; Console.WriteLine("Tabla: " + reader.GetString(0)); }
            Console.WriteLine("Tablas existentes: " + count);
            return 0;
        }
        catch (MySqlException e)
        {
            Console.WriteLine("No se pudo verificar MySQL. Código: " + e.Number + ". Revisá red, credenciales y CA.");
            return 1;
        }
    }
}
