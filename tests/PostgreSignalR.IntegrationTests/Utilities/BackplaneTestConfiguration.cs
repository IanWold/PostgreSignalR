namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// Describes a non-default backplane configuration for a test server pair. Instances are compared by value,
/// so <see cref="ContainerFixture.GetServerPairAsync"/> can use a configuration as a cache key to reuse the
/// same server pair across every test that requests the same configuration.
/// </summary>
public sealed record BackplaneTestConfiguration(
    string? Prefix = null,
    ChannelNameNormaization? ChannelNameNormaization = null,
    bool UseTableStrategy = true,
    PayloadTableStorage? PayloadTableStorage = null
)
{
    public static BackplaneTestConfiguration Default { get; } = new();

    internal IReadOnlyDictionary<string, string> ToEnvironmentVariables()
    {
        var environment = new Dictionary<string, string>
        {
            ["UseTableStrategy"] = UseTableStrategy.ToString()
        };

        if (Prefix is not null)
        {
            environment["Backplane__Prefix"] = Prefix;
        }

        if (ChannelNameNormaization is not null)
        {
            environment["Backplane__ChannelNameNormaization"] = ChannelNameNormaization.Value.ToString();
        }

        if (PayloadTableStorage is not null)
        {
            environment["PayloadTable__StorageMode"] = PayloadTableStorage.Value.ToString();
        }

        return environment;
    }
}
