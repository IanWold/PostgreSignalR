using Npgsql;
using PostgreSignalR;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(dataSource)
    // Ensure you call AddBackplaneTablePayloadStrategy after AddPostgresBackplane
    .AddBackplaneTablePayloadStrategy(options =>
    {
        // The storage mode determines when the paylod table is used to store payloads:
        //     * Auto: only uses the payload table when the payloads are too large
        //     * Always: uses the payload table for all payloads
        // Auto is the default, since most workloads should not have large payloads.
        options.StorageMode = PostgresBackplanePayloadTableStorage.Auto;
        // options.StorageMode = PostgresBackplanePayloadTableStorage.Always;
        
        
        
        // Allows you to specify the schema name where you want the table to be.
        // By default, this is empty, so the table will be in the default schema
        //     for your connection string.
        options.SchemaName = "backplane";

        // Allows you to specify the name of the table:
        options.TableName = "payloads";



        // Automatic cleanup is built-in to the table payload strategy, but inefficient.
        // This cleanup runs on your server: PostgreSignalR keeps a Timer running
        //     and periodically dispatches DELETE commands to the database.
        // If AutomaticCleanup is false, this does not happen.
        // The default value is true, as if it was false then this strategy would
        //     indefinitely accumulate records in the table by default.
        // If you are implementing your own table with a more robust cleanup
        //     (or if you want to preserve all messages)
        //     then set this to false.
        options.AutomaticCleanup = true;
        // options.AutomaticCleanup = false;

        // The age (in milliseconds) that a payload record needs to be
        //     in order to be deleted by automatic cleanup.
        // Default is 5000 ms (5 seconds).
        options.AutomaticCleanupTtlMs = 1000;

        // The period between DELETE commands being sent to the database.
        // This is the interval set on the Timer by the table payload strategy.
        // The default is 21600000, or 6 hours.
        options.AutomaticCleanupIntervalMs = 3600000;
    });

var app = builder.Build();

// If you do not want to implement your own payload table, PostgreSignalR includes a default.
// So long as the connection string you use for PostgreSignalR can create a table,
//     call this during your application startup to ensure the payload table is created.
// This will create a table and an index on its created_at column.
// Do not call this if you are implementing your own table.
await app.InitializePostgresBackplanePayloadTableAsync();

app.MapHub<ChatHub>("/chat");

app.Run();
