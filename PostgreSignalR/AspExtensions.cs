using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSignalR;

namespace Microsoft.Extensions.DependencyInjection;

public static class AspExtensions
{
    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="postgresConnectionString">The connection string used to connect to the Postgres database.</param>
    /// <returns>The same instance of the <see cref="ISignalRServerBuilder"/> for chaining.</returns>
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, NpgsqlDataSource dataSource) =>
        AddPostgresBackplane(signalrBuilder, o => o.DataSource = dataSource);

    /// <summary>
    /// Adds scale-out to a <see cref="ISignalRServerBuilder"/>, using a shared Postgres database.
    /// </summary>
    /// <param name="signalrBuilder">The <see cref="ISignalRServerBuilder"/>.</param>
    /// <param name="postgresConnectionString">The connection string used to connect to the Postgres database.</param>
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
    public static ISignalRServerBuilder AddPostgresBackplane(this ISignalRServerBuilder signalrBuilder, Action<PostgresBackplaneOptions> configure)
    {
        signalrBuilder.Services.Configure(configure);
        signalrBuilder.Services.AddSingleton(typeof(HubLifetimeManager<>), typeof(PostgresHubLifetimeManager<>));
        
        return signalrBuilder;
    }

    /// <summary>
    /// Initializes a standard payload table for the Postgres backplane.
    /// </summary>
    /// <param name="builder">The <see cref="IApplicationBuilder"/>.</param>
    /// <param name="ct">The optional <see cref="CancellationToken"/>.</param>
    public static async Task InitializePostgresBackplanePayloadTableAsync(this IApplicationBuilder builder, CancellationToken ct = default)
    {
        var options = builder.ApplicationServices.GetRequiredService<IOptions<PostgresBackplaneOptions>>().Value;
        var tableName = options.PayloadTable.QualifiedTableName;
        await PostgresPayloadTableHelper.CreateTableAsync(tableName, options.DataSource, ct);
    }
}
