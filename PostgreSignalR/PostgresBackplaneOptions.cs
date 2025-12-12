using Npgsql;

namespace PostgreSignalR;

/// <summary>
/// Strategies the Postgres backplane can use to send payloads for events.
/// </summary>
public enum PostgresBackplanePayloadStrategy
{
    /// <summary>
    /// Always sends payloads directly in the notification event payload.
    /// </summary>
    /// <remarks>
    /// Postgres limits event payloads to 8000 bytes, this option will fail for larger payloads.
    /// </remarks>
    AlwaysUseEvent,

    /// <summary>
    /// Always stores paylaods in the payload table and sends as references in the notification event payload.
    /// </summary>
    /// <remarks>
    /// The payload table can be configured via <see cref="PostgresBackplaneOptions.PayloadTable"/>
    /// </remarks>
    AlwaysUseTable,

    /// <summary>
    /// If the payload is less than 8000 bytes, sends directly in the notification event payload.
    /// Otherwise, stores paylaods in the payload table and sends as references in the notification event payload.
    /// </summary>
    /// <remarks>
    /// The payload table can be configured via <see cref="PostgresBackplaneOptions.PayloadTable"/>
    /// </remarks>
    UseTableWhenLarge
}

/// <summary>
/// Options to configure the Postgres backplane
/// </summary>
public class PostgresBackplaneOptions
{
    /// <summary>
    /// Options to configure the payload table used if <see cref="PayloadStrategy"/> is <see cref="PostgresBackplanePayloadStrategy.AlwaysUseTable"/> or <see cref="PostgresBackplanePayloadStrategy.UseTableWhenLarge"/>.
    /// </summary>
    public class PayloadTableOptions
    {
        /// <summary>
        /// The name of the schema containing the table.
        /// </summary>
        /// <remarks>
        /// If null, the default schema will be used.
        /// </remarks>
        /// <value>
        /// Default: <c>null</c>
        /// </value>
        public string? SchemaName { get; set; }

        /// <summary>
        /// The name of the table.
        /// </summary>
        /// <value>
        /// Default: <c>"backplane_payloads"</c>
        /// </value>
        public string TableName { get; set; } = "backplane_payloads";

        /// <summary>
        /// Whether PostgreSignalR should peform automatic cleanup of payload records.
        /// </summary>
        /// <remarks>
        /// Automatic cleanup works by keeping a <see cref="System.Timers.Timer"/> and periodically clearing the table.
        /// </remarks>
        /// <value>
        /// Default: <c>true</c>
        /// </value>
        public bool AutomaticCleanup { get; set; } = true;

        /// <summary>
        /// The minimum TTL of a payload record in the table in milliseconds.
        /// </summary>
        /// <value>
        /// Default: <c>1000</c>
        /// </value>
        public int AutomaticCleanupTtlMs { get; set; } = 1000;

        /// <summary>
        /// The interval between cleanups in milliseconds.
        /// </summary>
        /// <value>
        /// Default: <c>360000</c>
        /// </value>
        public int AutomaticCleanupIntervalMs { get; set; } = 360000;
    }

    /// <summary>
    /// Configures a prefix for Postgres notification channel names.
    /// </summary>
    /// <remarks>
    /// If multiple apps are using the same database for notifications, each should have a different prefix.
    /// </remarks>
    /// <value>
    /// Default: <c>"postgresignalr"</c>
    /// </value>
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
    /// </summary>
    /// <remarks>
    /// The backplane will not initialize until the hub receives a connection.
    /// If you need to eagerly initialize the backplane, resolve <see cref="PostgresHubLifetimeManager{THub}"/> for the Hub type and call <see cref="PostgresHubLifetimeManager{THub}.EnsureInitializedAsync"/>.
    /// </remarks>
    /// <value>
    /// Default: <c>null</c>
    /// </value>
    public Action? OnInitialized { get; set; }

    /// <summary>
    /// Configures how payloads are sent in notifications.
    /// </summary>
    /// <remarks>
    /// Possible values:
    /// <list type="bullet">
    ///     <item><see cref="PostgresBackplanePayloadStrategy.AlwaysUseEvent"/>: Always uses the event payload and fails when payload is greater than 8000 bytes</item>
    ///     <item><see cref="PostgresBackplanePayloadStrategy.AlwaysUseTable"/>: Always uses a table to store payloads, configured by <see cref="PayloadTable"/></item>
    ///     <item><see cref="PostgresBackplanePayloadStrategy.UseTableWhenLarge"/>: Uses the event payload if the payload is less than 8000 bytes, otherwise uses a table to store payloads, configured by <see cref="PayloadTable"/></item>
    /// </list>
    /// </remarks>
    /// <value>
    /// Default: <see cref="PostgresBackplanePayloadStrategy.AlwaysUseEvent"/>
    /// </value>
    public PostgresBackplanePayloadStrategy PayloadStrategy { get; set; } = PostgresBackplanePayloadStrategy.AlwaysUseEvent;

    /// <summary>
    /// Configures a table used to store payloads if <see cref="PayloadStrategy"/> is <see cref="PostgresBackplanePayloadStrategy.AlwaysUseTable"/> or <see cref="PostgresBackplanePayloadStrategy.UseTableWhenLarge"/>.
    /// </summary>
    /// <remarks>
    /// The table can be automatically created via <c>app.UsePostgresBackplanePayloadTable()</c>.
    /// When <see cref="PayloadStrategy"/> is <see cref="PostgresBackplanePayloadStrategy.AlwaysUseEvent"/> the table is not used.
    /// </remarks>
    public PayloadTableOptions PayloadTable { get; set; } = new();
}
