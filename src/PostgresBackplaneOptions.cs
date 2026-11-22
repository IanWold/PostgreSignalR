using Npgsql;

namespace PostgreSignalR;

/// <summary>
/// Postgres limits channel names to identifiers up to 63 characters.
/// To guarantee this constraint is met, the backplane normalizes channel names.
/// </summary>
public enum ChannelNameNormaization
{
    /// <summary>
    /// Only applies normalization to channel names greater than 63 characters.
    /// For large names, the last 8 characters will be a hash of the channel name
    /// </summary>
    Truncate,

    /// <summary>
    /// Always hashes the channel name. The prefix is not modified.
    /// </summary>
    HashAlways
}

/// <summary>
/// How the backplane maps connection, user and group sends onto Postgres channels.
/// </summary>
public enum ChannelRouting
{
    /// <summary>
    /// A channel per connection, user and group. Each server LISTENs on the channels
    /// of its local targets.
    /// </summary>
    PerTarget,

    /// <summary>
    /// One shared channel for every connection, user and group send. Each server LISTENs
    /// on a fixed set of channels and delivers to its own local targets from in-memory state.
    /// </summary>
    Shared
}

/// <summary>
/// Handler for <see cref="PostgresBackplaneOptions.OnInitialized"/>
/// </summary>
public delegate void OnInitializedHandler();

/// <summary>
/// Handler for <see cref="PostgresBackplaneOptions.OnReconnected"/>
/// </summary>
public delegate void OnReconnectedHandler();

/// <summary>
/// Options to configure the Postgres backplane
/// </summary>
public class PostgresBackplaneOptions
{
    /// <summary>
    /// Configures a prefix for Postgres notification channel names.
    /// Default: <c>"backplane"</c>
    /// </summary>
    /// <remarks>
    /// If multiple apps are using the same database for notifications, each should have a different prefix.
    /// </remarks>
    public string Prefix { get; set; } = "backplane";

    /// <summary>
    /// Configures how the backplane normalizes channel names.
    /// Possible values:
    /// <list type="bullet">
    ///     <item><see cref="ChannelNameNormaization.Truncate"/>: Truncates long channel names with a short hash. Better for performance.</item>
    ///     <item><see cref="ChannelNameNormaization.HashAlways"/>: Always hashes channel names. Better for safety.</item>
    /// </list>
    /// Default: <see cref="ChannelNameNormaization.Truncate"/>
    /// </summary>
    public ChannelNameNormaization ChannelNameNormaization { get; set; } = ChannelNameNormaization.Truncate;

    /// <summary>
    /// Configures how connection, user and group sends are routed between servers.
    /// Possible values:
    /// <list type="bullet">
    ///     <item><see cref="ChannelRouting.PerTarget"/>: A channel per connection, user and group. Each server receives only the messages for its own targets.</item>
    ///     <item><see cref="ChannelRouting.Shared"/>: One shared channel. Each server LISTENs on a fixed set of channels, and connecting or joining a group never touches Postgres.</item>
    /// </list>
    /// Default: <see cref="ChannelRouting.PerTarget"/>
    /// </summary>
    /// <remarks>
    /// Servers using different routing don't exchange messages, so every server sharing a <see cref="Prefix"/> must use the same value.
    /// With <see cref="ChannelRouting.Shared"/>, each hub's channels are also kept separate from other hubs'.
    /// </remarks>
    public ChannelRouting ChannelRouting { get; set; } = ChannelRouting.PerTarget;

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

    /// <summary>
    /// Configures a callback invoked each time the backplane reconnects to Postgres after losing its connection.
    /// Default: <c>null</c>
    /// </summary>
    /// <remarks>
    /// Messages sent while the backplane was disconnected are lost, so use this to resync anything clients may have missed.
    /// Each hub has its own connection, so this is invoked once for each hub that reconnects.
    /// </remarks>
    public event OnReconnectedHandler? OnReconnected;

    internal void InvokeOnInitialized() =>
        OnInitialized?.Invoke();

    internal void InvokeOnReconnected() =>
        OnReconnected?.Invoke();

    internal bool IsValid(out string? message)
    {
        message = null;

        if (Prefix.Length >= 20)
        {
            message = "Prefix must be less than 20 characters.";
        }
        else if (DataSource is null)
        {
            message = "DataSource must be configured.";
        }
        else if (!Enum.IsDefined(ChannelRouting))
        {
            message = $"ChannelRouting value {(int)ChannelRouting} is not valid.";
        }

        return message is null;
    }
}
