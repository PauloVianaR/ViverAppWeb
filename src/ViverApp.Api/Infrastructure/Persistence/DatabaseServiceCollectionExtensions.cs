using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Infrastructure.Persistence;

internal static class DatabaseServiceCollectionExtensions
{
    internal const string DevelopmentDatabase = "viverappweb";
    internal const string HomologationDatabase = "viverappweb_homolog";

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
        var requiredDatabase = RequiredDatabase(configuration);
        if (!string.Equals(builder.Database, requiredDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A API recusou o database configurado. O único alvo permitido neste modo é {requiredDatabase}.");
        }

        services.AddDbContextPool<ViverAppDbContext>(options =>
            options.UseMySQL(builder.ConnectionString));
        services.AddHostedService<DatabaseCompatibilityHostedService>();

        return services;
    }

    internal static string RequiredDatabase(IConfiguration configuration)
    {
        if (!configuration.GetValue("Homologation:Enabled", false))
            return DevelopmentDatabase;

        var unsafeSettings = new List<string>();
        if (configuration.GetValue("Authentication:Delivery:Enabled", true))
            unsafeSettings.Add("Authentication:Delivery:Enabled");
        if (configuration.GetValue("Notifications:BusinessDelivery:Enabled", false))
            unsafeSettings.Add("Notifications:BusinessDelivery:Enabled");
        if (configuration.GetValue("Notifications:Scheduler:Enabled", false))
            unsafeSettings.Add("Notifications:Scheduler:Enabled");
        if (configuration.GetValue("PagBank:Enabled", false))
            unsafeSettings.Add("PagBank:Enabled");
        if (!string.Equals(configuration["Storage:Private:Provider"], "Database", StringComparison.OrdinalIgnoreCase))
            unsafeSettings.Add("Storage:Private:Provider");
        if (!string.IsNullOrWhiteSpace(configuration["GoogleOAuth:ClientID"]))
            unsafeSettings.Add("GoogleOAuth:ClientID");
        if (unsafeSettings.Count != 0)
        {
            throw new InvalidOperationException(
                $"Homologação recusada: desabilite {string.Join(", ", unsafeSettings)}.");
        }

        return HomologationDatabase;
    }
}
