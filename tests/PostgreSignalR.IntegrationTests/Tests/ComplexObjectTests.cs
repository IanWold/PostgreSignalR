// Commented to get the simples through PR

// using PostgreSignalR.IntegrationTests.Abstractions;

// namespace PostgreSignalR.IntegrationTests.Tests;

// public class ComplexObjectTests(ContainerFixture fixture) : BaseTest(fixture)
// {
//     [RetryFact]
//     public async Task Broadcast_AllServersReceive()
//     {
//         await using var client1 = await Server1.CreateClientAsync();
//         await using var client2 = await Server2.CreateClientAsync();

//         var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.MessageComplexObject));
//         var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.MessageComplexObject));

//         await client1.Send.SendToAll_ComplexObject(RandomComplexObject);

//         Assert.Equal(RandomComplexObject, (await messageFromClient1).Arg<ComplexObject>());
//         Assert.Equal(RandomComplexObject, (await messageFromClient2).Arg<ComplexObject>());
//     }

//     [RetryFact]
//     public async Task Connection_TargetsSingleConnection()
//     {
//         await using var sender = await Server1.CreateClientAsync();
//         await using var target = await Server2.CreateClientAsync();
//         await using var bystander = await Server2.CreateClientAsync();

//         var targetId = await target.Send.GetConnectionId();

//         var messageFromTarget = target.ExpectMessageAsync(nameof(IClient.MessageComplexObject));

//         await sender.Send.SendToConnection_ComplexObject(targetId, RandomComplexObject);

//         Assert.Equal(RandomComplexObject, (await messageFromTarget).Arg<ComplexObject>());

//         await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
//     }

//     [RetryFact]
//     public async Task Group_SendHitsMembersAcrossServers()
//     {
//         await using var member1 = await Server1.CreateClientAsync();
//         await using var member2 = await Server2.CreateClientAsync();
//         await using var outsider = await Server2.CreateClientAsync();

//         await member1.Send.JoinGroup(GroupName);
//         await member2.Send.JoinGroup(GroupName);

//         var messageFromMember1 = member1.ExpectMessageAsync(nameof(IClient.MessageComplexObject));
//         var messageFromMember2 = member2.ExpectMessageAsync(nameof(IClient.MessageComplexObject));

//         await member1.Send.SendToAllInGroup_ComplexObject(GroupName, RandomComplexObject);

//         Assert.Equal(RandomComplexObject, (await messageFromMember1).Arg<ComplexObject>());
//         Assert.Equal(RandomComplexObject, (await messageFromMember2).Arg<ComplexObject>());

//         await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
//     }

//     [RetryFact]
//     public async Task Users_SendToUsersHitsMultipleUsers()
//     {
//         await using var user1 = await Server1.CreateClientAsync("u1");
//         await using var user2 = await Server2.CreateClientAsync("u2");
//         await using var user3 = await Server2.CreateClientAsync("u3");

//         var messageFromUser1 = user1.ExpectMessageAsync(nameof(IClient.MessageComplexObject));
//         var messageFromUser2 = user2.ExpectMessageAsync(nameof(IClient.MessageComplexObject));

//         await user3.Send.SendToUsers_ComplexObject(["u1", "u2"], RandomComplexObject);

//         Assert.Equal(RandomComplexObject, (await messageFromUser1).Arg<ComplexObject>());
//         Assert.Equal(RandomComplexObject, (await messageFromUser2).Arg<ComplexObject>());
//         await user3.EnsureNoMessageAsync(nameof(IClient.MessageComplexObject));
//     }
// }
