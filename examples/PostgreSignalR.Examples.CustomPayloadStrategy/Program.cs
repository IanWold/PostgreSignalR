using Npgsql;
using PostgreSignalR;
using PostgreSignalR.Examples.CustomPayloadStrategy;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

DatabaseMigrator.Migrate(dataSource, "./Migrations");

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(dataSource);

builder.Services.AddSingleton<IPostgresBackplanePayloadStrategy, CustomPayloadStrategy>();

var app = builder.Build();

app.MapHub<ChatHub>("/chat");

app.Run();
