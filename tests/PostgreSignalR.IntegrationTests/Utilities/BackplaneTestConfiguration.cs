namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// Describes a non-default backplane configuration for a test server pair. Instances are compared by value,
/// so <see cref="ContainerFixture.GetServerPairAsync(BackplaneTestConfiguration)"/> can use a configuration as a cache key to reuse the
/// same server pair across every test that requests the same configuration.
/// </summary>
public sealed record BackplaneTestConfiguration(
    string? Prefix = null,
    ChannelNameNormaization? ChannelNameNormaization = null,
    bool UseTableStrategy = true,
    PayloadTableStorage? PayloadTableStorage = null,
    bool? AutomaticCleanup = null,
    int? AutomaticCleanupTtlMs = null,
    int? AutomaticCleanupIntervalMs = null,
    ChannelRouting? ChannelRouting = null,
    CustomPayloadStrategy? CustomPayloadStrategy = null,
    BackplaneHubOptIn? BackplaneHubOptIn = null
)
{
    public static BackplaneTestConfiguration Default { get; } = new();

    /// <summary>
    /// The default configuration, with <see cref="PostgreSignalR.ChannelRouting.Shared"/> routing.
    /// </summary>
    public static BackplaneTestConfiguration Shared { get; } = new(ChannelRouting: PostgreSignalR.ChannelRouting.Shared);

    internal IReadOnlyDictionary<string, string> ToEnvironmentVariables()
    {
        var environment = new Dictionary<string, string>
        {
            ["UseTableStrategy"] = UseTableStrategy.ToString()
        };

        if (CustomPayloadStrategy is not null)
        {
            environment["CustomPayloadStrategy"] = CustomPayloadStrategy.Value.ToString();
        }

        if (BackplaneHubOptIn is not null)
        {
            environment["BackplaneHubOptIn"] = BackplaneHubOptIn.Value.ToString();
        }

        if (Prefix is not null)
        {
            environment["Backplane__Prefix"] = Prefix;
        }

        if (ChannelNameNormaization is not null)
        {
            environment["Backplane__ChannelNameNormaization"] = ChannelNameNormaization.Value.ToString();
        }

        if (ChannelRouting is not null)
        {
            environment["Backplane__ChannelRouting"] = ChannelRouting.Value.ToString();
        }

        if (PayloadTableStorage is not null)
        {
            environment["PayloadTable__StorageMode"] = PayloadTableStorage.Value.ToString();
        }

        if (AutomaticCleanup is not null)
        {
            environment["PayloadTable__AutomaticCleanup"] = AutomaticCleanup.Value.ToString();
        }

        if (AutomaticCleanupTtlMs is not null)
        {
            environment["PayloadTable__AutomaticCleanupTtlMs"] = AutomaticCleanupTtlMs.Value.ToString();
        }

        if (AutomaticCleanupIntervalMs is not null)
        {
            environment["PayloadTable__AutomaticCleanupIntervalMs"] = AutomaticCleanupIntervalMs.Value.ToString();
        }

        return environment;
    }
}

/// <summary>
/// A payload strategy defined in the test app rather than the library, as an app's own would be.
/// </summary>
public enum CustomPayloadStrategy
{
    /// <summary>
    /// Implements only <see cref="IPayloadStrategy"/>.
    /// </summary>
    Plain,

    /// <summary>
    /// Implements <see cref="IPrefixedPayloadStrategy"/>.
    /// </summary>
    Prefixed
}

/// <summary>
/// Opts the test hub in to the backplane with <c>AddBackplaneHub</c>, leaving the second hub off it.
/// </summary>
public enum BackplaneHubOptIn
{
    /// <summary>
    /// Opts in after calling <c>AddPostgresBackplane</c>.
    /// </summary>
    AfterAddPostgresBackplane,

    /// <summary>
    /// Opts in before calling <c>AddPostgresBackplane</c>.
    /// </summary>
    BeforeAddPostgresBackplane
}
