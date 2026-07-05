namespace PostgreSignalR.IntegrationTests;

public abstract class PostgresRestartTestBase(PostgresRestartFixture fixture) : TestData, IClassFixture<PostgresRestartFixture>
{
    internal TestServer Server1 => fixture.Server1!;
    internal TestServer Server2 => fixture.Server2!;

    protected Task RestartPostgresAsync() =>
        fixture.RestartPostgresAsync();
}
