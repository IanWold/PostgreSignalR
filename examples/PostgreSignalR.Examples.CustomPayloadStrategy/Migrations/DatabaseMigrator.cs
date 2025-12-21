using Npgsql;

namespace PostgreSignalR.Examples.CustomPayloadStrategy;

public static class DatabaseMigrator
{
    static NpgsqlCommand GetCommand(string query, NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        var command = connection.CreateCommand();

        command.Connection = connection;
        command.CommandText = query;

        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    static void Command(this NpgsqlConnection connection, NpgsqlTransaction transaction, string query)
    {
        var command = GetCommand(query, connection, transaction);
        command.ExecuteNonQuery();
    }

    static T? Query<T>(NpgsqlConnection connection, string query)
    {
        var command = GetCommand(query, connection);
        return (T?)command.ExecuteScalar();
    }

    static long GetLatestVersion(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var migrationHistoryExists = Query<bool>(
            connection,
            "SELECT EXISTS(SELECT 1 FROM pg_tables WHERE tablename = 'migration_history')"
        );

        if (migrationHistoryExists)
        {
            return Query<long>(
                connection,
                "SELECT MAX(version) FROM migration_history"
            );
        }
        else
        {
            Command(connection, transaction,
                """
                DROP SCHEMA public CASCADE;
                CREATE SCHEMA public;
                GRANT ALL ON SCHEMA public TO postgres;
                GRANT ALL ON SCHEMA public TO public;
                COMMENT ON SCHEMA public IS 'standard public schema';

                CREATE TABLE migration_history(
                    "version" bigint primary key,
                    "migrated" timestamp default NOW()
                )
                """
            );

            return -1;
        }
    }

    static IEnumerable<(int, string)> GetNewMigrationFiles(string migrationsDirectory, long latestVersion) =>
        new DirectoryInfo(migrationsDirectory).GetFiles()
        .Where(f => f.Extension == ".sql")
        .Select(f => (version: Convert.ToInt32(f.Name.Split('_')[0]), file: f.FullName))
        .Where(f => f.version > latestVersion)
        .OrderBy(f => f.version);

    static void RunMigrationFile((int version, string name) file, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var query = "";
        using (var reader = new StreamReader(file.name))
        {
            query = reader.ReadToEnd();
        }

        connection.Command(transaction, query);
        connection.Command(transaction, $"INSERT INTO migration_history (version) VALUES ({file.version})");
    }

    public static void Migrate(NpgsqlDataSource dataSource, string migrationsDirectory)
    {
        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var latestVersion = GetLatestVersion(connection, transaction);
        var newMigrationFiles = GetNewMigrationFiles(migrationsDirectory, latestVersion);

        foreach (var file in newMigrationFiles)
        {
            RunMigrationFile(file, connection, transaction);
        }

        transaction.Commit();
        connection.Close();
    }
}
