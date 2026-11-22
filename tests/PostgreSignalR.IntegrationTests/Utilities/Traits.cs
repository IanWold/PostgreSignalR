namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// Test traits. Filter on them with <c>--filter-trait "Category=PostgresCompat"</c>.
/// </summary>
internal static class Traits
{
    public const string Category = "Category";

    /// <summary>
    /// Tests of behavior that could change between Postgres versions: LISTEN and UNLISTEN timing and churn, reconnecting, payload size limits, and the payload table.
    /// CI runs these on every supported Postgres version.
    /// </summary>
    public const string PostgresCompat = "PostgresCompat";
}
