namespace PostgreSignalR.IntegrationTests;

internal static class TestTimeouts
{
    public static readonly TimeSpan ConnectionStartTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultMessageTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DisconnectSettleDelay = TimeSpan.FromMilliseconds(150);
    public const int HealthCheckMaxAttempts = 120;
    public static readonly TimeSpan HealthCheckPollInterval = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan NegativeAssertionTimeout = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan RetryAttemptTimeout = TimeSpan.FromSeconds(5);
}
