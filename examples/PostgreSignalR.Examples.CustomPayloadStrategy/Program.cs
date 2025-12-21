using Npgsql;
using PostgreSignalR;
using PostgreSignalR.Examples.CustomPayloadStrategy;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

// Sample migration code; sets up the custom table.
// See https://gist.github.com/IanWold/d466f0e7e983da7b09e5ecc6bf719341
DatabaseMigrator.Migrate(dataSource, "./Migrations");

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(dataSource);

// Here we manually register the custom payload strategy.
// Be sure to register this after calling AddPostgresBackplane.
// It does not matter whether you register this as singleton, transient, or scoped,
//     the hub lifetime manager will resolve this immediately and keep a single instance,
//     so it will effectively be singleton in memory anyway.
// If you have multiple hubs and some particular construction logic in the strategy,
//     it may be necessary to register this as transient.
builder.Services.AddSingleton<IPostgresBackplanePayloadStrategy, CustomPayloadStrategy>();

var app = builder.Build();

app.MapHub<ChatHub>("/chat");

app.Run();
