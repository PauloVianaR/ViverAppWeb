using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Infrastructure.Persistence;

internal sealed class DatabaseCompatibilityHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IHostedService
{
    private const string RequiredServerVersion = "8.0.41";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ViverAppDbContext>();
        var connection = context.Database.GetDbConnection();

        await connection.OpenAsync(cancellationToken);
        try
        {
            var database = await ExecuteScalarAsync(connection, "SELECT DATABASE()", cancellationToken);
            var requiredDatabase = DatabaseServiceCollectionExtensions.RequiredDatabase(configuration);
            if (!string.Equals(database, requiredDatabase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"A API recusou o database conectado. O único alvo permitido neste modo é {requiredDatabase}.");
            }

            var version = await ExecuteScalarAsync(connection, "SELECT VERSION()", cancellationToken);
            if (!IsRequiredServerVersion(version))
            {
                throw new InvalidOperationException(
                    $"A API exige MySQL {RequiredServerVersion}; o servidor conectado é incompatível.");
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsRequiredServerVersion(string version)
    {
        return string.Equals(version, RequiredServerVersion, StringComparison.Ordinal)
            || version.StartsWith($"{RequiredServerVersion}-", StringComparison.Ordinal)
            || version.StartsWith($"{RequiredServerVersion}+", StringComparison.Ordinal);
    }

    private static async Task<string> ExecuteScalarAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
                   await command.ExecuteScalarAsync(cancellationToken),
                   CultureInfo.InvariantCulture)
               ?? string.Empty;
    }
}
