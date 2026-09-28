using MySql.Data.MySqlClient;

namespace ViverApp.Database;

internal static partial class Program
{
    private static async Task<int> AnalyzeHotPathsAsync(MySqlConnectionStringBuilder source, CancellationToken ct)
    {
        await using var connection = CreateConnection(source, TargetDatabase);
        await connection.OpenAsync(ct);
        await AssertServerAsync(connection, ct);
        await AssertDatabaseAsync(connection, TargetDatabase, ct);

        var from = DateTime.UtcNow.Date;
        var to = from.AddDays(30);
        var queries = new (string Name, string Sql)[]
        {
            ("agenda_clinica", "SELECT id FROM appointments WHERE starts_at_utc >= @from AND starts_at_utc < @to ORDER BY starts_at_utc LIMIT 12"),
            ("agenda_profissional", "SELECT id FROM appointments WHERE professional_account_id = (SELECT MIN(account_id) FROM professional_profiles) AND starts_at_utc >= @from AND starts_at_utc < @to ORDER BY starts_at_utc LIMIT 12"),
            ("pagamentos_reconciliacao", "SELECT id FROM payments WHERE status_code = 'pending' AND next_reconciliation_at_utc <= @to ORDER BY next_reconciliation_at_utc LIMIT 20"),
            ("outbox_pendente", "SELECT id FROM outbox_messages WHERE status_code = 'pending' AND next_attempt_at_utc <= @to ORDER BY id LIMIT 1"),
            ("jobs_pendentes", "SELECT id FROM scheduled_jobs WHERE status_code = 'pending' AND next_attempt_at_utc <= @to ORDER BY due_at_utc, id LIMIT 1"),
        };
        foreach (var (name, sql) in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN " + sql;
            command.Parameters.AddWithValue("@from", from);
            command.Parameters.AddWithValue("@to", to);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var accessIndex = reader.GetOrdinal("type");
                var access = reader.IsDBNull(accessIndex) ? "indefinido" : reader.GetString(accessIndex);
                var keyIndex = reader.GetOrdinal("key");
                var key = reader.IsDBNull(keyIndex) ? "nenhum" : reader.GetString(keyIndex);
                var rowsIndex = reader.GetOrdinal("rows");
                var estimatedRows = reader.IsDBNull(rowsIndex) ? "indefinido" : reader.GetValue(rowsIndex).ToString();
                var tableIndex = reader.GetOrdinal("table");
                var table = reader.IsDBNull(tableIndex) ? "indefinido" : reader.GetString(tableIndex);
                Console.WriteLine($"{name}: tabela={table}, acesso={access}, indice={key}, linhas_estimadas={estimatedRows}");
            }
        }
        return 0;
    }
}
