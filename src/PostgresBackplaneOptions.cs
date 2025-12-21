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

    internal bool IsValid(out string? message)
    {
        message = null;

        if (Prefix.Length >= 20)
        {
            message = "Prefix must be less than 20 characters.";
        }

        return message is null;
    }
}
