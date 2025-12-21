using Npgsql;
using PostgreSignalR;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(dataSource)
    .AddBackplaneTablePayloadStrategy(options =>
    {
        options.StorageMode = PostgresBackplanePayloadTableStorage.Auto;
        options.SchemaName = "backplane";
        options.TableName = "payloads";
        options.AutomaticCleanup = true;
        options.AutomaticCleanupTtlMs = 1000;
        options.AutomaticCleanupIntervalMs = 3600000;
    });

var app = builder.Build();

await app.InitializePostgresBackplanePayloadTableAsync();

app.MapHub<ChatHub>("/chat");

app.Run();
