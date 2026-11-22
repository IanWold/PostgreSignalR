using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// A message sent to a target right after it becomes reachable must arrive. Under per-target routing, that needs
/// the target's LISTEN to have run before connecting or joining completes; under shared routing, it needs the
/// shared channel's LISTEN to have run before the backplane reports it's initialized.
/// </summary>
/// <remarks>
/// These use <see cref="FactAttribute"/> rather than <see cref="RetryFactAttribute"/>, and repeat each attempt,
/// because a retry would hide the race they're checking for.
/// </remarks>
public abstract class FirstMessageTests(ContainerFixture fixture, BackplaneTestConfiguration configuration) : ConfigurableBaseTest(fixture, configuration)
{
    private const int Attempts = 20;

    [Fact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task Connection_ReceivesMessageSentRightAfterConnecting()
    {
        await using var sender = await Server2.CreateClientAsync();

        for (var i = 0; i < Attempts; i++)
        {
            await using var target = await Server1.CreateClientAsync(waitForHealthy: false);

            var messageToTarget = target.ExpectMessageAsync(nameof(IClient.Message));

            await sender.Send.SendToConnection(target.ConnectionId, ShortMessage);

            Assert.Equal(ShortMessage, (await messageToTarget).Arg<string>());
        }
    }

    [Fact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task User_ReceivesMessageSentRightAfterConnecting()
    {
        await using var sender = await Server2.CreateClientAsync();

        for (var i = 0; i < Attempts; i++)
        {
            var userId = $"first-{Guid.NewGuid()}";
            await using var target = await Server1.CreateClientAsync(userId, waitForHealthy: false);

            var messageToTarget = target.ExpectMessageAsync(nameof(IClient.Message));

            await sender.Send.SendToUser(userId, ShortMessage);

            Assert.Equal(ShortMessage, (await messageToTarget).Arg<string>());
        }
    }

    [Fact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task Group_ReceivesMessageSentRightAfterJoining()
    {
        await using var sender = await Server2.CreateClientAsync();
        await using var member = await Server1.CreateClientAsync();

        for (var i = 0; i < Attempts; i++)
        {
            var groupName = Guid.NewGuid().ToString();

            await member.Send.JoinGroup(groupName);

            var messageToMember = member.ExpectMessageAsync(nameof(IClient.Message));

            await sender.Send.SendToAllInGroup(groupName, ShortMessage);

            Assert.Equal(ShortMessage, (await messageToMember).Arg<string>());
        }
    }

    [Fact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task Group_ReceivesMessageSentRightAfterBeingAddedFromAnotherServer()
    {
        await using var sender = await Server2.CreateClientAsync();
        await using var member = await Server1.CreateClientAsync();

        for (var i = 0; i < Attempts; i++)
        {
            var groupName = Guid.NewGuid().ToString();

            await sender.Send.AddConnectionToGroup(member.ConnectionId, groupName);

            var messageToMember = member.ExpectMessageAsync(nameof(IClient.Message));

            await sender.Send.SendToAllInGroup(groupName, ShortMessage);

            Assert.Equal(ShortMessage, (await messageToMember).Arg<string>());
        }
    }
}

public sealed class PerTargetFirstMessageTests(ContainerFixture fixture) : FirstMessageTests(fixture, BackplaneTestConfiguration.Default);

public sealed class SharedFirstMessageTests(ContainerFixture fixture) : FirstMessageTests(fixture, BackplaneTestConfiguration.Shared);
