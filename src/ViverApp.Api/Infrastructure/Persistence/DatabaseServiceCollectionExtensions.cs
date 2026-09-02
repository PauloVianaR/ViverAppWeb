using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Infrastructure.Persistence;

internal static class DatabaseServiceCollectionExtensions
{
    private const string RequiredDatabase = "viverappweb";

    public static IServiceCollection AddViverAppDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LocalConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:LocalConnection não está configurada em user-secrets ou no ambiente.");
        }

        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Database, RequiredDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A API recusou o database configurado. O único alvo permitido é {RequiredDatabase}.");
        }

        services.AddDbContextPool<ViverAppDbContext>(options =>
            options.UseMySQL(builder.ConnectionString));
        services.AddHostedService<DatabaseCompatibilityHostedService>();

        return services;
    }
}
