using System.Globalization;
using LocalProductManager.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.Length == 2 && args[0] == "--cleanup-tests")
{
    var cleanupConnection = Environment.GetEnvironmentVariable("LPM_PG_MIGRATION_CONNECTION");
    if (string.IsNullOrWhiteSpace(cleanupConnection))
    {
        Console.Error.WriteLine("LPM_PG_MIGRATION_CONNECTION is required.");
        return 2;
    }

    await CleanupTestDataAsync(Path.GetFullPath(args[1]), cleanupConnection);
    return 0;
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: migration <sqlite-database-path>");
    return 2;
}

var postgresConnection = Environment.GetEnvironmentVariable("LPM_PG_MIGRATION_CONNECTION");
if (string.IsNullOrWhiteSpace(postgresConnection))
{
    Console.Error.WriteLine("LPM_PG_MIGRATION_CONNECTION is required.");
    return 2;
}

var sqlitePath = Path.GetFullPath(args[0]);
if (!File.Exists(sqlitePath))
{
    Console.Error.WriteLine($"SQLite database not found: {sqlitePath}");
    return 2;
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(postgresConnection)
    .Options;
await using (var db = new AppDbContext(options))
{
    await db.Database.EnsureCreatedAsync();
}

await using var source = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
await using var destination = new NpgsqlConnection(postgresConnection);
await source.OpenAsync();
await destination.OpenAsync();

var sqliteTables = new List<string>();
await using (var command = source.CreateCommand())
{
    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) sqliteTables.Add(reader.GetString(0));
}

var postgresTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
await using (var command = new NpgsqlCommand(
    "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'",
    destination))
await using (var reader = await command.ExecuteReaderAsync())
{
    while (await reader.ReadAsync()) postgresTables.Add(reader.GetString(0));
}

await using var transaction = await destination.BeginTransactionAsync();
await new NpgsqlCommand("SET LOCAL session_replication_role = replica", destination, transaction).ExecuteNonQueryAsync();
var totalRows = 0;

foreach (var table in sqliteTables.Where(postgresTables.Contains))
{
    var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    await using (var typeCommand = new NpgsqlCommand("""
        SELECT column_name, data_type
        FROM information_schema.columns
        WHERE table_schema='public' AND table_name=@table
        ORDER BY ordinal_position
        """, destination, transaction))
    {
        typeCommand.Parameters.AddWithValue("table", table);
        await using var typeReader = await typeCommand.ExecuteReaderAsync();
        while (await typeReader.ReadAsync()) types[typeReader.GetString(0)] = typeReader.GetString(1);
    }

    await using var readCommand = source.CreateCommand();
    readCommand.CommandText = $"SELECT * FROM {Quote(table)}";
    await using var reader = await readCommand.ExecuteReaderAsync();
    var columns = Enumerable.Range(0, reader.FieldCount)
        .Select(reader.GetName)
        .Where(types.ContainsKey)
        .ToArray();
    var ordinals = columns.ToDictionary(column => column, reader.GetOrdinal);
    var insertSql = $"INSERT INTO {Quote(table)} ({string.Join(",", columns.Select(Quote))}) VALUES ({string.Join(",", columns.Select((_, i) => $"@p{i}"))})";
    var rows = 0;

    while (await reader.ReadAsync())
    {
        await using var insert = new NpgsqlCommand(insertSql, destination, transaction);
        for (var i = 0; i < columns.Length; i++)
        {
            var raw = reader.GetValue(ordinals[columns[i]]);
            insert.Parameters.AddWithValue($"p{i}", ConvertValue(raw, types[columns[i]]));
        }
        await insert.ExecuteNonQueryAsync();
        rows++;
    }

    if (columns.Contains("Id", StringComparer.OrdinalIgnoreCase))
    {
        var sequenceName = await new NpgsqlCommand(
            $"SELECT pg_get_serial_sequence('{table}', 'Id')",
            destination,
            transaction).ExecuteScalarAsync();
        if (sequenceName is string sequence)
        {
            var sequenceSql = $"""
                SELECT setval(
                    '{sequence}',
                    COALESCE((SELECT MAX("Id") FROM {Quote(table)}), 1),
                    EXISTS(SELECT 1 FROM {Quote(table)})
                )
                """;
            await new NpgsqlCommand(sequenceSql, destination, transaction).ExecuteScalarAsync();
        }
    }

    totalRows += rows;
    Console.WriteLine($"{table}: {rows}");
}

await transaction.CommitAsync();
Console.WriteLine($"Migration complete: {sqliteTables.Count(table => postgresTables.Contains(table))} tables, {totalRows} rows.");
return 0;

static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

static object ConvertValue(object value, string dataType)
{
    if (value is DBNull) return DBNull.Value;
    return dataType switch
    {
        "boolean" => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
        "integer" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
        "bigint" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        "numeric" => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        "real" => Convert.ToSingle(value, CultureInfo.InvariantCulture),
        "double precision" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        "timestamp with time zone" => DateTimeOffset.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime(),
        "timestamp without time zone" => DateTime.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind),
        "date" => DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
        _ => value
    };
}

static async Task CleanupTestDataAsync(string sqlitePath, string postgresConnection)
{
    const string sqliteSql = """
        PRAGMA foreign_keys=ON;
        DELETE FROM products
        WHERE Sku LIKE 'TEST-%' OR Sku LIKE 'API-TEST-%' OR Sku LIKE 'FULL-%'
           OR ProductName IN ('Concurrent inventory test product', 'API smoke test product', 'Full API test product');
        DELETE FROM warehouses WHERE Name IN ('Isolated test warehouse', 'Test warehouse');
        DELETE FROM social_accounts WHERE AccountName='Test channel' OR AccountHandle='@test';
        DELETE FROM video_trend_items WHERE Title='Test video' AND Description='Isolated';
        """;
    var backupPath = Path.Combine(
        Path.GetDirectoryName(sqlitePath)!,
        $"local-product-manager-before-test-cleanup-{DateTime.Now:yyyyMMdd-HHmmss}.db");
    await using (var sqlite = new SqliteConnection($"Data Source={sqlitePath}"))
    {
        await sqlite.OpenAsync();
        await using (var backup = new SqliteConnection($"Data Source={backupPath}"))
        {
            await backup.OpenAsync();
            sqlite.BackupDatabase(backup);
        }
        await using var transaction = await sqlite.BeginTransactionAsync();
        await using var command = sqlite.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sqliteSql;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    const string postgresSql = """
        DELETE FROM products
        WHERE "Sku" LIKE 'TEST-%' OR "Sku" LIKE 'API-TEST-%' OR "Sku" LIKE 'FULL-%'
           OR "ProductName" IN ('Concurrent inventory test product', 'API smoke test product', 'Full API test product');
        DELETE FROM warehouses WHERE "Name" IN ('Isolated test warehouse', 'Test warehouse');
        DELETE FROM social_accounts WHERE "AccountName"='Test channel' OR "AccountHandle"='@test';
        DELETE FROM video_trend_items WHERE "Title"='Test video' AND "Description"='Isolated';
        """;
    await using var postgres = new NpgsqlConnection(postgresConnection);
    await postgres.OpenAsync();
    await using var pgTransaction = await postgres.BeginTransactionAsync();
    await new NpgsqlCommand(postgresSql, postgres, pgTransaction).ExecuteNonQueryAsync();
    await pgTransaction.CommitAsync();
    Console.WriteLine($"Test-tagged data removed from SQLite and PostgreSQL. Backup: {backupPath}");
}
