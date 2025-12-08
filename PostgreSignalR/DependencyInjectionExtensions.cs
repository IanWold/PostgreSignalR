using Microsoft.AspNetCore.SignalR;
using PostgreSignalR;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="postgresConnectionString">The connection string used to connect to the Postgres database.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, string postgresConnectionString) =>
        AddPostgresBackplane(signalrBuilder, o =>
            o.ConnectionString = postgresConnectionString
        );

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="postgresConnectionString">The connection string used to connect to the Postgres database.</param>
    /// <param name="configure">A callback to configure the Postgres options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, string postgresConnectionString, Action<PostgresOptions> configure) =>
        AddPostgresBackplane(signalrBuilder, o => {
            o.ConnectionString = postgresConnectionString;
            configure(o);
        });

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="configure">A callback to configure the Postgres options.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, Action<PostgresOptions> configure)
    {
        signalrBuilder.Services.Configure(configure);
        signalrBuilder.Services.AddSingleton(typeof(HubLifetimeManager<>), typeof(PostgresHubLifetimeManager<>));
        
        return signalrBuilder;
    }
}
