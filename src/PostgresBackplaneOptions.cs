using Npgsql;

namespace PostgreSignalR;

/// <summary>
/// Handler for <see cref="PostgresBackplaneOptions.OnInitialized"/>
/// </summary>
public delegate void OnInitializedHandler();

/// <summary>
/// Options to configure the Postgres backplane
/// </summary>
public class PostgresBackplaneOptions
{
    /// <summary>
    /// Configures a prefix for Postgres notification channel names.
    /// Default: <c>"postgresignalr"</c>
    /// </summary>
    /// <remarks>
    /// If multiple apps are using the same database for notifications, each should have a different prefix.
    /// </remarks>
    public string Prefix { get; set; } = "postgresignalr";

    /// <summary>
    /// Configures the <see href="https://www.npgsql.org/doc/api/Npgsql.NpgsqlDataSource.html">NpgsqlDataSource</see> to connect to the Postgres database.
    /// </summary>
    /// <remarks>
    /// Can be built using <see href="https://www.npgsql.org/doc/api/Npgsql.NpgsqlDataSourceBuilder.html">NpgsqlDataSourceBuilder</see>.
    /// </remarks>
    public required NpgsqlDataSource DataSource { get; set; }

    /// <summary>
    /// Configures a callback invoked once the backplane is initialized for the first time.
    /// Default: <c>null</c>
    /// </summary>
    /// <remarks>
    /// The backplane will not initialize until the hub receives a connection.
    /// If you need to eagerly initialize the backplane, resolve <see cref="PostgresHubLifetimeManager{THub}"/> for the Hub type and call <see cref="PostgresHubLifetimeManager{THub}.EnsureInitializedAsync"/>.
    /// </remarks>
    public event OnInitializedHandler? OnInitialized;

    internal void InvokeOnInitialized() =>
        OnInitialized?.Invoke();
}
