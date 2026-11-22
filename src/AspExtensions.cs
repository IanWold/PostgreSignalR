using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using PostgreSignalR;

namespace Microsoft.Extensions.DependencyInjection;

public static class AspExtensions
{
    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="connectionString">The connection string used to connect to the Postgres database.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, string connectionString) =>
        AddPostgresBackplane(signalrBuilder, o => o.DataSource = new NpgsqlDataSourceBuilder(connectionString).Build());

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="dataSource">The <see cref="Npgsql.NpgsqlDataSource"/> used to connect to the Postgres database.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, NpgsqlDataSource dataSource) =>
        AddPostgresBackplane(signalrBuilder, o => o.DataSource = dataSource);

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="connectionString">The connection string used to connect to the Postgres database.</param>
    /// <param name="configure">A callback to configure the Postgres options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, string connectionString, Action<PostgresBackplaneOptions> configure) =>
        AddPostgresBackplane(signalrBuilder, o =>
        {
            o.DataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
            configure(o);
        });

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="dataSource">The <see cref="Npgsql.NpgsqlDataSource"/> used to connect to the Postgres database.</param>
    /// <param name="configure">A callback to configure the Postgres options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, NpgsqlDataSource dataSource, Action<PostgresBackplaneOptions> configure) =>
        AddPostgresBackplane(signalrBuilder, o =>
        {
            o.DataSource = dataSource;
            configure(o);
        });

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="configure">A callback to configure the Postgres options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, Action<PostgresBackplaneOptions> configure) =>
        AddPostgresBackplane(signalrBuilder, (o, _) => configure(o));

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="configure">A callback to configure the Postgres options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, Action<PostgresBackplaneOptions, IServiceProvider> configure)
    {
        signalrBuilder.Services.AddOptions<PostgresBackplaneOptions>().Configure(configure);

        signalrBuilder.Services.AddSingleton<IPayloadStrategy, EventPayloadStrategy>();

        // Every hub uses the backplane, unless hubs have been chosen with AddBackplaneHub.
        if (!signalrBuilder.Services.Any(IsSingleHubRegistration))
        {
            signalrBuilder.Services.AddSingleton(typeof(HubLifetimeManager<>), typeof(PostgresHubLifetimeManager<>));
        }

        return signalrBuilder;
    }

    /// <summary>
    /// Uses the Postgres backplane for <typeparamref name="THub"/>.
    /// Once any hub is added this way, only the added hubs use the backplane. The others use SignalR's default lifetime manager,
    /// so their messages only reach connections on the same server.
    /// </summary>
    /// <remarks>
    /// If no hubs are added, every hub uses the backplane.
    /// This still requires <c>AddPostgresBackplane</c>, and can be called before or after it.
    /// </remarks>
    /// <typeparam name="THub">The hub to use the backplane for.</typeparam>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddBackplaneHub<THub>(this ISignalRServerBuilder signalrBuilder) where THub : Hub
    {
        var services = signalrBuilder.Services;

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (IsAllHubsRegistration(services[i]))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton<HubLifetimeManager<THub>, PostgresHubLifetimeManager<THub>>();

        return signalrBuilder;
    }

    private static bool IsAllHubsRegistration(ServiceDescriptor descriptor) =>
        !descriptor.IsKeyedService
        && descriptor.ServiceType == typeof(HubLifetimeManager<>)
        && descriptor.ImplementationType == typeof(PostgresHubLifetimeManager<>);

    private static bool IsSingleHubRegistration(ServiceDescriptor descriptor) =>
        !descriptor.IsKeyedService
        && descriptor.ImplementationType is { IsConstructedGenericType: true } implementationType
        && implementationType.GetGenericTypeDefinition() == typeof(PostgresHubLifetimeManager<>);

    /// <summary>
    /// Configures the Postgres backplane to store payloads in a table.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddBackplaneTablePayloadStrategy(this ISignalRServerBuilder signalrBuilder) =>
        signalrBuilder.AddBackplaneTablePayloadStrategy(o => { });

    /// <summary>
    /// Configures the Postgres backplane to store payloads in a table.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="configure">A callback to configure the payload table options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddBackplaneTablePayloadStrategy(this ISignalRServerBuilder signalrBuilder, Action<PayloadTableOptions> configure)
    {
        signalrBuilder.Services.Configure(configure);

        signalrBuilder.Services.AddSingleton<IPayloadStrategy, TablePayloadStrategy>();

        return signalrBuilder;
    }

    /// <summary>
    /// Initializes a standard payload table for the Postgres backplane.
    /// </summary>
    /// <param name="builder">The <see cref="IApplicationBuilder"/>.</param>
    /// <param name="ct">The optional <see cref="CancellationToken"/>.</param>
    public static async Task InitializePostgresBackplanePayloadTableAsync(this IApplicationBuilder builder, CancellationToken ct = default)
    {
        var strategy = builder.ApplicationServices.GetRequiredService<IPayloadStrategy>();

        if (strategy is TablePayloadStrategy tableStrategy)
        {
            await tableStrategy.InitializeTableAsync(ct);
        }
    }
}
