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
        options.StorageMode = PostgresBackplanePayloadTableStorage.Always;
        options.TableName = "custom_payloads";
        options.AutomaticCleanup = false;
    });

var app = builder.Build();

app.MapHub<ChatHub>("/chat");

app.Run();
