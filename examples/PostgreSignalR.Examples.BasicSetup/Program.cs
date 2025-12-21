using Npgsql;
using PostgreSignalR;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("backplane") ?? throw new Exception("Postgres backplane connection string required.");
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();

builder.Services
    .AddSignalR()
    .AddPostgresBackplane(dataSource, options =>
    {
        options.Prefix = "myapp";
        options.ChannelNameNormaization = ChannelNameNormaization.Truncate;
        options.OnInitialized += () => { /* Do something */ };
    });

var app = builder.Build();

app.MapHub<ChatHub>("/chat");

app.Run();
