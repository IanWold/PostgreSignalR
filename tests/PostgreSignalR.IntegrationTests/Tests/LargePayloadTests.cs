using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public abstract class LargePayloadTests(ContainerFixture fixture, BackplaneTestConfiguration configuration) : ConfigurableBaseTest(fixture, configuration)
{
    [RetryFact]
    public async Task LargePayload_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(LongMessage);

        Assert.Equal(LongMessage, (await messageFromClient1).Arg<string>());
        Assert.Equal(LongMessage, (await messageFromClient2).Arg<string>());
    }

    [RetryFact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task LargePayload_ConnectionReceives()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var messageToTarget = target.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnection(target.ConnectionId, LongMessage);

        Assert.Equal(LongMessage, (await messageToTarget).Arg<string>());

        await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task LargePayload_GroupAndUserReceive()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var member = await Server2.CreateClientAsync();
        var userId = $"large-{Guid.NewGuid()}";
        await using var user = await Server2.CreateClientAsync(userId);

        await member.Send.JoinGroup(GroupName);

        var messageToMember = member.ExpectMessageAsync(nameof(IClient.Message));
        var messageToUser = user.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAllInGroup(GroupName, LongMessage);
        await sender.Send.SendToUsers([userId], LongMessage);

        Assert.Equal(LongMessage, (await messageToMember).Arg<string>());
        Assert.Equal(LongMessage, (await messageToUser).Arg<string>());
    }

    /// <summary>
    /// Each message holds a JSON and a MessagePack copy of the string, so these lengths make messages of roughly 5600 to 6400 bytes:
    /// either side of the 5997 byte limit for sending a message in the notification rather than the payload table.
    /// </summary>
    [Fact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task Payload_SizesAroundTheInlineLimitAreDelivered()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();

        for (var length = 2750; length <= 3150; length++)
        {
            var message = new string('A', length);
            var messageToTarget = target.ExpectMessageAsync(nameof(IClient.Message));

            await sender.Send.SendToConnection(target.ConnectionId, message);

            Assert.Equal(message, (await messageToTarget).Arg<string>());
        }
    }
}

public sealed class PerTargetLargePayloadTests(ContainerFixture fixture) : LargePayloadTests(fixture, BackplaneTestConfiguration.Default);

public sealed class SharedLargePayloadTests(ContainerFixture fixture) : LargePayloadTests(fixture, BackplaneTestConfiguration.Shared);
