using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Gamexid.Services;

public class GamexidDbContextFactory : IDesignTimeDbContextFactory<GamexidDbContext>
{
    public GamexidDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("Gamexid.Api/appsettings.json", optional: true)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(DatabaseConfiguration.SecretsId)
            .AddEnvironmentVariables()
            .Build();
        var database = DatabaseConfiguration.Load(config);
        var options = new DbContextOptionsBuilder<GamexidDbContext>();
        database.Configure(options);
        return new GamexidDbContext(options.Options);
    }
}
