using Npgsql;

namespace PostgreSignalR.IntegrationTests;

public class DatabaseContainer : IAsyncLifetime
{
    private readonly Func<string> _getConnectionString;
    private readonly string _uniqueName = Guid.NewGuid().ToString("N");
    private readonly string? _username;
    private readonly string? _password;

    public DatabaseContainer(Func<string> getConnectionString)
    {
        _getConnectionString = getConnectionString;

        // Captured once, up front, rather than read fresh from getConnectionString() like the
        // host/port below - unlike the host-mapped port, credentials don't change across a
        // Postgres restart, and ConnectionStringInternal needs to remain usable (e.g. to build
        // an app container's connection string) even while Postgres is stopped and its mapped
        // port is temporarily unavailable.
        var builder = new NpgsqlConnectionStringBuilder(getConnectionString());
        _username = builder.Username;
        _password = builder.Password;
    }

    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_getConnectionString())
        {
            Database = _uniqueName
        }
        .ConnectionString;

    public string ConnectionStringInternal =>
        new NpgsqlConnectionStringBuilder
        {
            Username = _username,
            Password = _password,
            Database = _uniqueName,
            Host = "postgres_network",
            Port = 5432
        }
        .ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_getConnectionString());
        await connection.OpenAsync();

        await using var createDatabaseCommand = connection.CreateCommand();
        createDatabaseCommand.CommandText = $"CREATE DATABASE {EscapeIdentifier(_uniqueName)};";
        await createDatabaseCommand.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_getConnectionString());
        await connection.OpenAsync();

        await using (var terminateBackendCommand = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName;", connection))
        {
            terminateBackendCommand.Parameters.Add(new("databaseName", _uniqueName));
            await terminateBackendCommand.ExecuteNonQueryAsync();
        }

        await using var dropDatabaseCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS {EscapeIdentifier(_uniqueName)};", connection);
        await dropDatabaseCommand.ExecuteNonQueryAsync();

        GC.SuppressFinalize(this);
    }

    private static string EscapeIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
