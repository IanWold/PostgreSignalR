using Npgsql;

namespace PostgreSignalR.IntegrationTests;

public class DatabaseContainer(string connectionString) : IAsyncLifetime
{
    private readonly string _uniqueName = Guid.NewGuid().ToString("N");

    public string ConnectionString => 
        new NpgsqlConnectionStringBuilder(connectionString) 
        {
            Database = _uniqueName
        }
        .ConnectionString;

    public string ConnectionStringInternal =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = _uniqueName,
            Host = "postgres_network",
            Port = 5432
        }
        .ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var createDatabaseCommand = connection.CreateCommand();
        createDatabaseCommand.CommandText = $"CREATE DATABASE \"{_uniqueName}\";";
        await createDatabaseCommand.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var terminateBackendCommand = new NpgsqlCommand($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_uniqueName}';", connection))
        {
            await terminateBackendCommand.ExecuteNonQueryAsync();
        }

        await using var dropDatabaseCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_uniqueName}\";", connection);
        await dropDatabaseCommand.ExecuteNonQueryAsync();

        GC.SuppressFinalize(this);
    }
}
