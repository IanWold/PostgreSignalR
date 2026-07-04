using Microsoft.AspNetCore.SignalR;
using Npgsql;
using PostgreSignalR.IntegrationTests.App;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres") ?? throw new Exception();

var isBackplaneReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

var backplaneConfiguration = builder.Configuration.GetSection("Backplane");
var payloadTableConfiguration = builder.Configuration.GetSection("PayloadTable");
var useTableStrategy = builder.Configuration.GetValue("UseTableStrategy", true);

var signalRBuilder = builder.Services.AddSignalR()
    .AddPostgresBackplane(new NpgsqlDataSourceBuilder(postgresConnectionString).Build(), o =>
    {
        backplaneConfiguration.Bind(o);
        o.OnInitialized += () => isBackplaneReady.TrySetResult();
    });

if (useTableStrategy)
{
    signalRBuilder.AddBackplaneTablePayloadStrategy(o => payloadTableConfiguration.Bind(o));
}

builder.Services.AddSingleton<IUserIdProvider, QueryStringUserIdProvider>();

var app = builder.Build();

await app.InitializePostgresBackplanePayloadTableAsync();

app.UseRouting();
app.MapHub<TestHub>("/hub");
app.MapGet("/health", () =>
    isBackplaneReady.Task.IsCompleted
    ? Results.Ok("ready")
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.Run();
