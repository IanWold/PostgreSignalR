namespace PostgreSignalR;

/// <summary>
/// Storage options for <see cref="TablePayloadStrategy"/>
/// </summary>
public enum PostgresBackplanePayloadTableStorage
{
    /// <summary>
    /// If the payload is less than 8000 bytes, sends directly in the notification event payload.
    /// Otherwise, stores paylaods in the payload table and sends as references in the notification event payload.
    /// </summary>
    Auto,

    /// <summary>
    /// Always stores paylaods in the payload table and sends as references in the notification event payload.
    /// </summary>
    Always
}

/// <summary>
/// Options to configure the payload table used by <see cref="TablePayloadStrategy"/>
/// </summary>
public class PostgresBackplanePayloadTableOptions
{
    /// <summary>
    /// Configures how the table is used to store payloads.
    /// Possible values:
    /// <list type="bullet">
    ///     <item><see cref="PostgresBackplanePayloadTableStorage.Auto"/>: Uses the event payload if the payload is less than 8000 bytes, otherwise uses a table to store payloads</item>
    ///     <item><see cref="PostgresBackplanePayloadTableStorage.Always"/>: Always uses a table to store payloads</item>
    /// </list>
    /// Default: <see cref="PostgresBackplanePayloadTableStorage.Auto"/>
    /// </summary>
    public PostgresBackplanePayloadTableStorage StorageMode = PostgresBackplanePayloadTableStorage.Auto;

    /// <summary>
    /// The name of the schema containing the table.
    /// Default: <c>null</c>
    /// </summary>
    /// <remarks>
    /// If null, the default schema will be used.
    /// </remarks>
    public string? SchemaName { get; set; }

    /// <summary>
    /// The name of the table.
    /// Default: <c>"backplane_payloads"</c>
    /// </summary>
    public string TableName { get; set; } = "backplane_payloads";

    /// <summary>
    /// Whether PostgreSignalR should peform automatic cleanup of payload records.
    /// Default: <c>true</c>
    /// </summary>
    /// <remarks>
    /// Automatic cleanup works by keeping a <see cref="System.Timers.Timer"/> and periodically clearing the table.
    /// </remarks>
    public bool AutomaticCleanup { get; set; } = true;

    /// <summary>
    /// The minimum TTL of a payload record in the table in milliseconds.
    /// Default: <c>300000</c> (5 minutes)
    /// </summary>
    public int AutomaticCleanupTtlMs { get; set; } = 1000;

    /// <summary>
    /// The interval between cleanups in milliseconds.
    /// Default: <c>21600000</c> (6 hours)
    /// </summary>
    public int AutomaticCleanupIntervalMs { get; set; } = 21600000;

    internal string QualifiedTableName =>
        $"{(SchemaName is not null ? $"\"{SchemaName}\"." : string.Empty)}\"{TableName}\"";
}
