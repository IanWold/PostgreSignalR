using Npgsql;
using PostgreSignalR;
using PostgreSignalR.Examples.CustomTable;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

DatabaseMigrator.Migrate(dataSource, "./Migrations");

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(dataSource)
    .AddBackplaneTablePayloadStrategy(options =>
    {
        // Storage mode determines when paylaods are written to the payload table.
        // Always will write all payloads to the table,
        //     Auto will only write large payloads to the table.
        options.StorageMode = PostgresBackplanePayloadTableStorage.Always;
        // options.StorageMode = PostgresBackplanePayloadTableStorage.Auto;



        // SchemaName and Table name should be configured to identify where
        //     your table is:
        options.SchemaName = null;
        options.TableName = "custom_payloads";



        // This example shows how to set up a custom table with a pg_cron job
        //     to provide automatic cleanup.
        // In this case, AutomaticCleanup should be configured to false
        //     so that the built-in automatic cleanup does not run.
        options.AutomaticCleanup = false;
    });

var app = builder.Build();

app.MapHub<ChatHub>("/chat");

app.Run();
