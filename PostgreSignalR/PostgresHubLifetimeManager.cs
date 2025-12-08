using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace PostgreSignalR;

file class PostgresFeature
{
    public HashSet<string> Groups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public class PostgresHubLifetimeManager<THub> : HubLifetimeManager<THub>, IDisposable where THub : Hub
{
    private readonly HubConnectionStore _connections = new();
    private readonly PostgresSubscriptionManager _groups = new();
    private readonly PostgresSubscriptionManager _users = new();
    private readonly AckHandler _ackHandler;
    private readonly PostgresChannels _channels;
    private readonly IHubProtocolResolver _hubProtocolResolver;
    private readonly ILogger<PostgresHubLifetimeManager<THub>> _logger;
    private readonly PostgresOptions _options;
    private readonly string _serverName = GenerateServerName();
    private readonly PostgresProtocol _protocol;
    private readonly SemaphoreSlim _commandLock = new(1);
    private readonly ConcurrentDictionary<string, Action<byte[]>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly ClientResultsManager _clientResultsManager = new();
    private readonly PostgresListener _postgresListener;
    private int _internalAckId;
    private bool _isInitialized;

    public PostgresHubLifetimeManager(
        ILogger<PostgresHubLifetimeManager<THub>> logger,
        IOptions<PostgresOptions> options,
        IHubProtocolResolver hubProtocolResolver,
        IOptions<HubOptions>? globalHubOptions,
        IOptions<HubOptions<THub>>? hubOptions
    )
    {
        _hubProtocolResolver = hubProtocolResolver;
        _logger = logger;
        _options = options.Value;
        _ackHandler = new AckHandler();
        _channels = new PostgresChannels(_options.Prefix, _serverName);

        if (globalHubOptions != null && hubOptions != null)
        {
            _protocol = new PostgresProtocol(hubProtocolResolver, globalHubOptions.Value.SupportedProtocols, hubOptions.Value.SupportedProtocols);
        }
        else
        {
            var supportedProtocols = hubProtocolResolver.AllProtocols.Select(p => p.Name).ToList();
            _protocol = new PostgresProtocol(hubProtocolResolver, supportedProtocols, null);
        }

        _postgresListener = new(options.Value.ConnectionString);
        _postgresListener.OnNotification += OnNotification;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        _logger.BackplaneInitializing();

        try
        {
            await _postgresListener.EnsureReadyAsync();
            await SubscribeToAll();
            await SubscribeToGroupManagementChannel();
            await SubscribeToAckChannel();
            await SubscribeToReturnResultsAsync();

            if (_options.OnInitialized is not null)
            {
                try
                {
                    _options.OnInitialized.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Postgres backplane OnInitialized callback failed.");
                }
            }

            _logger.BackplaneInitialized();
        }
        catch (Exception ex)
        {
            _logger.BackplaneUnableInitialize(ex);
        }
    }

    public override async Task OnConnectedAsync(HubConnectionContext connection)
    {
        await EnsureInitializedAsync();

        var feature = new PostgresFeature();
        connection.Features.Set(feature);

        var userTask = Task.CompletedTask;

        _connections.Add(connection);

        var connectionTask = SubscribeToConnection(connection);

        if (!string.IsNullOrEmpty(connection.UserIdentifier))
        {
            userTask = SubscribeToUser(connection);
        }

        await Task.WhenAll(connectionTask, userTask);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(HubConnectionContext connection)
    {
        await EnsureInitializedAsync();

        _connections.Remove(connection);

        _logger.BackplaneUnsubscribingConnection(connection.ConnectionId);

        var tasks = new List<Task>
        {
            UnsubscribeAsync(_channels.Connection(connection.ConnectionId))
        };

        var feature = connection.Features.GetRequiredFeature<PostgresFeature>();
        var groupNames = feature.Groups;

        if (groupNames != null)
        {
            // Copy the groups to an array here because they get removed from this collection
            // in RemoveFromGroupAsync
            foreach (var group in groupNames.ToArray())
            {
                // Use RemoveGroupAsyncCore because the connection is local and we don't want to
                // accidentally go to other servers with our remove request.
                tasks.Add(RemoveGroupAsyncCore(connection, group));
            }
        }

        if (!string.IsNullOrEmpty(connection.UserIdentifier))
        {
            tasks.Add(RemoveUserAsync(connection));
        }

        await Task.WhenAll(tasks);
    }

    /// <inheritdoc />
    public override async Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var message = _protocol.WriteInvocation(methodName, args);
        await PublishAsync(_channels.All, message);
    }

    /// <inheritdoc />
    public override async Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var message = _protocol.WriteInvocation(methodName, args, excludedConnectionIds: excludedConnectionIds);
        await PublishAsync(_channels.All, message);
    }

    /// <inheritdoc />
    public override async Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(connectionId);

        // If the connection is local we can skip sending the message through the bus since we require sticky connections.
        // This also saves serializing and deserializing the message!
        var connection = _connections[connectionId];
        if (connection != null)
        {
            await connection.WriteAsync(new InvocationMessage(methodName, args), cancellationToken);
            return;
        }

        var message = _protocol.WriteInvocation(methodName, args);
        await PublishAsync(_channels.Connection(connectionId), message);
    }

    /// <inheritdoc />
    public override async Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(groupName);

        var message = _protocol.WriteInvocation(methodName, args);
        await PublishAsync(_channels.Group(groupName), message);
    }

    /// <inheritdoc />
    public override async Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(groupName);

        var message = _protocol.WriteInvocation(methodName, args, excludedConnectionIds: excludedConnectionIds);
        await PublishAsync(_channels.Group(groupName), message);
    }

    /// <inheritdoc />
    public override async Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var message = _protocol.WriteInvocation(methodName, args);
        await PublishAsync(_channels.User(userId), message);
    }

    /// <inheritdoc />
    public override async Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(groupName);

        var connection = _connections[connectionId];
        if (connection != null)
        {
            // short circuit if connection is on this server
            await AddGroupAsyncCore(connection, groupName);
            return;
        }

        await SendGroupActionAndWaitForAck(connectionId, groupName, GroupAction.Add);
    }

    /// <inheritdoc />
    public override async Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(groupName);

        var connection = _connections[connectionId];
        if (connection != null)
        {
            // short circuit if connection is on this server
            await RemoveGroupAsyncCore(connection, groupName);
            return;
        }

        await SendGroupActionAndWaitForAck(connectionId, groupName, GroupAction.Remove);
    }

    /// <inheritdoc />
    public override async Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(connectionIds);

        var publishTasks = new List<Task>(connectionIds.Count);
        var payload = _protocol.WriteInvocation(methodName, args);

        foreach (var connectionId in connectionIds)
        {
            publishTasks.Add(PublishAsync(_channels.Connection(connectionId), payload));
        }

        await Task.WhenAll(publishTasks);
    }

    /// <inheritdoc />
    public override async Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(groupNames);
        var publishTasks = new List<Task>(groupNames.Count);
        var payload = _protocol.WriteInvocation(methodName, args);

        foreach (var groupName in groupNames)
        {
            if (!string.IsNullOrEmpty(groupName))
            {
                publishTasks.Add(PublishAsync(_channels.Group(groupName), payload));
            }
        }

        await Task.WhenAll(publishTasks);
    }

    /// <inheritdoc />
    public override async Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        if (userIds.Count > 0)
        {
            await EnsureInitializedAsync();

            var payload = _protocol.WriteInvocation(methodName, args);
            var publishTasks = new List<Task>(userIds.Count);
            foreach (var userId in userIds)
            {
                if (!string.IsNullOrEmpty(userId))
                {
                    publishTasks.Add(PublishAsync(_channels.User(userId), payload));
                }
            }

            await Task.WhenAll(publishTasks);
            return;
        }
    }

    private async Task<long> PublishAsync(string channel, byte[] message)
    {
        _logger.BackplanePublishing(channel);

        var payload = Convert.ToBase64String(message);

        await _commandLock.WaitAsync();
        try
        {
            using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync();

            using var notifyCommand = new NpgsqlCommand($"NOTIFY {channel.EscapeQutoes()}, '{payload}';", connection);
            return await notifyCommand.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.BackplaneUnableToConnect(ex);
            throw;
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private Task AddGroupAsyncCore(HubConnectionContext connection, string groupName)
    {
        var feature = connection.Features.GetRequiredFeature<PostgresFeature>();
        var groupNames = feature.Groups;

        lock (groupNames)
        {
            // Connection already in group
            if (!groupNames.Add(groupName))
            {
                return Task.CompletedTask;
            }
        }

        var groupChannel = _channels.Group(groupName);
        return _groups.AddSubscriptionAsync(groupChannel, connection, SubscribeToGroupAsync);
    }

    /// <summary>
    /// This takes <see cref="HubConnectionContext"/> because we want to remove the connection from the
    /// _connections list in OnDisconnectedAsync and still be able to remove groups with this method.
    /// </summary>
    private async Task RemoveGroupAsyncCore(HubConnectionContext connection, string groupName)
    {
        var groupChannel = _channels.Group(groupName);

        await _groups.RemoveSubscriptionAsync(groupChannel, connection, this, (state, channelName) =>
        {
            var lifetimeManager = (PostgresHubLifetimeManager<THub>)state;
            lifetimeManager._logger.BackplaneUnsubscribingGroupChannel(groupChannel, channelName);
            return lifetimeManager.UnsubscribeAsync(channelName);
        });

        var feature = connection.Features.GetRequiredFeature<PostgresFeature>();
        var groupNames = feature.Groups;
        if (groupNames != null)
        {
            lock (groupNames)
            {
                groupNames.Remove(groupName);
            }
        }
    }

    private async Task SendGroupActionAndWaitForAck(string connectionId, string groupName, GroupAction action)
    {
        var id = Interlocked.Increment(ref _internalAckId);
        var ack = _ackHandler.CreateAck(id);
        // Send Add/Remove Group to other servers and wait for an ack or timeout
        var message = PostgresProtocol.WriteGroupCommand(new PostgresGroupCommand(id, _serverName, action, groupName, connectionId));
        await PublishAsync(_channels.GroupManagement, message);

        await ack;
    }

    private Task RemoveUserAsync(HubConnectionContext connection)
    {
        var userChannel = _channels.User(connection.UserIdentifier!);

        return _users.RemoveSubscriptionAsync(userChannel, connection, this, (state, channelName) =>
        {
            var lifetimeManager = (PostgresHubLifetimeManager<THub>)state;
            lifetimeManager._logger.BackplaneUnsubscribingUserChannel(userChannel, channelName);
            return lifetimeManager.UnsubscribeAsync(channelName);
        });
    }

    public void Dispose()
    {
        _ = _postgresListener.DisposeAsync();
        _ackHandler.Dispose();
    }

    private static CancellationTokenSource? CreateLinkedToken(CancellationToken token1, CancellationToken token2, out CancellationToken linkedToken)
    {
        if (!token1.CanBeCanceled)
        {
            linkedToken = token2;
            return null;
        }
        else if (!token2.CanBeCanceled)
        {
            linkedToken = token1;
            return null;
        }
        else
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(token1, token2);
            linkedToken = cts.Token;
            return cts;
        }
    }

    /// <inheritdoc/>
    public override async Task<T> InvokeConnectionAsync<T>(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync();

        // send thing
        ArgumentNullException.ThrowIfNull(connectionId);

        var connection = _connections[connectionId];

        // ID needs to be unique for each invocation and across servers, we generate a GUID every time, that should provide enough uniqueness guarantees.
        var invocationId = GenerateInvocationId();

        using var _ = CreateLinkedToken(cancellationToken, connection?.ConnectionAborted ?? default, out var linkedToken);
        var task = _clientResultsManager.AddInvocation<T>(connectionId, invocationId, linkedToken);

        try
        {
            if (connection == null)
            {
                // TODO: Need to handle other server going away while waiting for connection result
                var messageBytes = _protocol.WriteInvocation(methodName, args, invocationId, returnChannel: _channels.ReturnResults);
                await PublishAsync(_channels.Connection(connectionId), messageBytes);
            }
            else
            {
                // We're sending to a single connection
                // Write message directly to connection without caching it in memory
                var message = new InvocationMessage(invocationId, methodName, args);

                await connection.WriteAsync(message, cancellationToken);
            }
        }
        catch
        {
            _clientResultsManager.RemoveInvocation(invocationId);
            throw;
        }

        try
        {
            return await task;
        }
        catch
        {
            // ConnectionAborted will trigger a generic "Canceled" exception from the task, let's convert it into a more specific message.
            if (connection?.ConnectionAborted.IsCancellationRequested == true)
            {
                throw new IOException($"Connection '{connectionId}' disconnected.");
            }
            throw;
        }
    }

    /// <inheritdoc/>
    public override async Task SetConnectionResultAsync(string connectionId, CompletionMessage result)
    {
        await EnsureInitializedAsync();

        _clientResultsManager.TryCompleteResult(connectionId, result);
    }

    /// <inheritdoc/>
    public override bool TryGetReturnType(string invocationId, [NotNullWhen(true)] out Type? type)
    {
        return _clientResultsManager.TryGetType(invocationId, out type);
    }

    private async Task SubscribeToAll() => await SubscribeAsync(_channels.All, async message =>
    {
        try
        {
            _logger.BackplaneReceivedMessage(_channels.All);

            var invocation = PostgresProtocol.ReadInvocation(message);

            var tasks = new List<Task>(_connections.Count);

            foreach (var connection in _connections)
            {
                if (invocation.ExcludedConnectionIds == null || !invocation.ExcludedConnectionIds.Contains(connection.ConnectionId))
                {
                    tasks.Add(connection.WriteAsync(invocation.Message).AsTask());
                }
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.BackplaneFailedWritingMessage(ex);
        }
    });

    private async Task SubscribeToGroupManagementChannel() => await SubscribeAsync(_channels.GroupManagement, async message =>
    {
        try
        {
            var groupMessage = PostgresProtocol.ReadGroupCommand(message);

            var connection = _connections[groupMessage.ConnectionId];
            if (connection == null)
            {
                // user not on this server
                return;
            }

            if (groupMessage.Action == GroupAction.Remove)
            {
                await RemoveGroupAsyncCore(connection, groupMessage.GroupName);
            }

            if (groupMessage.Action == GroupAction.Add)
            {
                await AddGroupAsyncCore(connection, groupMessage.GroupName);
            }

            // Send an ack to the server that sent the original command.
            await PublishAsync(_channels.Ack(groupMessage.ServerName), PostgresProtocol.WriteAck(groupMessage.Id));
        }
        catch (Exception ex)
        {
            _logger.BackplaneInternalMessageFailed(ex);
        }
    });

    private async Task SubscribeToAckChannel() => await SubscribeAsync(_channels.Ack(_serverName), message =>
        _ackHandler.TriggerAck(PostgresProtocol.ReadAck(message))
    );

    private async Task SubscribeToConnection(HubConnectionContext connection) => await SubscribeAsync(_channels.Connection(connection.ConnectionId), async message =>
    {
        var invocation = PostgresProtocol.ReadInvocation(message);

        // This is a Client result we need to setup state for the completion and forward the message to the client
        if (!string.IsNullOrEmpty(invocation.InvocationId))
        {
            CancellationTokenRegistration? tokenRegistration = null;
            _clientResultsManager.AddInvocation(invocation.InvocationId, (typeof(RawResult), connection.ConnectionId, null, new Action<object, CompletionMessage>(async (object _, CompletionMessage completionMessage) =>
            {
                var protocolName = connection.Protocol.Name;
                tokenRegistration?.Dispose();

                var memoryBufferWriter = MemoryBufferWriter.Get();
                byte[] message;
                try
                {
                    try
                    {
                        connection.Protocol.WriteMessage(completionMessage, memoryBufferWriter);
                        message = PostgresProtocol.WriteCompletionMessage(memoryBufferWriter, protocolName);
                    }
                    finally
                    {
                        memoryBufferWriter.Dispose();
                    }

                    await PublishAsync(invocation.ReturnChannel!, message); // Ian: Why do we want to publish the message even if we got an exception writing it?
                }
                catch (Exception ex)
                {
                    _logger.BackplaneUnableForwardInvocation(completionMessage.InvocationId, ex);
                }
            })));

            // TODO: this isn't great
            // Ian: ^ This is one of my favorite comments in the .NET source
            tokenRegistration = connection.ConnectionAborted.UnsafeRegister(_ =>
            {
                var invocationInfo = _clientResultsManager.RemoveInvocation(invocation.InvocationId);
                invocationInfo?.Completion(null!, CompletionMessage.WithError(invocation.InvocationId, "Connection disconnected."));
            }, null);
        }

        // Forward message from other server to client
        // Normal client method invokes and client result invokes use the same message
        await connection.WriteAsync(invocation.Message);
    });

    private Task SubscribeToUser(HubConnectionContext connection)
    {
        var userChannel = _channels.User(connection.UserIdentifier!);

        return _users.AddSubscriptionAsync(userChannel, connection, async (channelName, subscriptions) =>
        {
            _logger.BackplaneSubscribingUserChannel(userChannel, channelName);
            await SubscribeAsync(channelName, async message =>
            {
                try
                {
                    var invocation = PostgresProtocol.ReadInvocation(message);

                    var tasks = new List<Task>(subscriptions.Count);
                    foreach (var userConnection in subscriptions)
                    {
                        tasks.Add(userConnection.WriteAsync(invocation.Message).AsTask());
                    }

                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger.BackplaneFailedWritingMessage(ex);
                }
            });
        });
    }

    private async Task SubscribeToGroupAsync(string groupChannel, HubConnectionStore groupConnections) => await SubscribeAsync(groupChannel, async message =>
    {
        try
        {
            var invocation = PostgresProtocol.ReadInvocation(message);

            var tasks = new List<Task>(groupConnections.Count);
            foreach (var groupConnection in groupConnections)
            {
                if (invocation.ExcludedConnectionIds?.Contains(groupConnection.ConnectionId) == true)
                {
                    continue;
                }

                tasks.Add(groupConnection.WriteAsync(invocation.Message).AsTask());
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.BackplaneFailedWritingMessage(ex);
        }
    });

    private async Task SubscribeToReturnResultsAsync() => await SubscribeAsync(_channels.ReturnResults, message =>
    {
        var completion = PostgresProtocol.ReadCompletion(message);
        IHubProtocol? protocol = null;
        foreach (var hubProtocol in _hubProtocolResolver.AllProtocols)
        {
            if (hubProtocol.Name.Equals(completion.ProtocolName))
            {
                protocol = hubProtocol;
                break;
            }
        }

        // Should only happen if you have different versions of servers and don't have the same protocols registered on both
        if (protocol is null)
        {
            _logger.BackplaneProtocolMismatch(completion.ProtocolName);
            return;
        }

        var ros = completion.CompletionMessage;
        HubMessage? hubMessage = null;
        bool retryForError = false;
        try
        {
            var parseSuccess = protocol.TryParseMessage(ref ros, _clientResultsManager, out hubMessage);
            retryForError = !parseSuccess;
        }
        catch
        {
            // Client returned wrong type? Or just an error from the HubProtocol, let's try with RawResult as the type and see if that works
            retryForError = true;
        }

        if (retryForError)
        {
            try
            {
                ros = completion.CompletionMessage;
                // if this works then we know there was an error with the type the client returned, we'll replace the CompletionMessage below and provide an error to the application code
                if (!protocol.TryParseMessage(ref ros, FakeInvocationBinder.Instance, out hubMessage))
                {
                    _logger.BackplaneErrorParsingResult(completion.ProtocolName);
                    return;
                }
            }
            // Exceptions here would mean the HubProtocol implementation very likely has a bug, the other server has already deserialized the message (with RawResult) so it should be deserializable
            // We don't know the InvocationId, we should let the application developer know and potentially surface the issue to the HubProtocol implementor
            catch (Exception ex)
            {
                _logger.BackplaneErrorParsingResult(completion.ProtocolName, ex);
                return;
            }
        }

        var invocationInfo = _clientResultsManager.RemoveInvocation(((CompletionMessage)hubMessage!).InvocationId!);

        if (retryForError && invocationInfo is not null)
        {
            hubMessage = CompletionMessage.WithError(((CompletionMessage)hubMessage!).InvocationId!, $"Client result wasn't deserializable to {invocationInfo?.Type.Name}.");
        }

        invocationInfo?.Completion(invocationInfo?.Tcs!, (CompletionMessage)hubMessage!);
    });

    private async Task SubscribeAsync(string channelName, Action<byte[]> handler)
    {
        if (_notificationHandlers.TryAdd(channelName, handler))
        {
            await _postgresListener.SubscribeAsync(channelName);
        }
    }

    private async Task UnsubscribeAsync(string channelName)
    {
        _ = _notificationHandlers.TryRemove(channelName, out _);
        await _postgresListener.UnsubscribeAsync(channelName);
    }

    private void OnNotification(object? sender, NpgsqlNotificationEventArgs e)
    {
        _logger.BackplaneReadNotification(e.Channel);
        if (_notificationHandlers.TryGetValue(e.Channel, out var handler))
        {
            handler(Convert.FromBase64String(e.Payload));
        }
    }

    private static string GenerateServerName()
    {
        // Use the machine name for convenient diagnostics, but add a guid to make it unique.
        // Example: MyServerName_02db60e5fab243b890a847fa5c4dcb29
        return $"{Environment.MachineName}_{Guid.NewGuid():N}";
    }

    private static string GenerateInvocationId()
    {
        Span<byte> buffer = stackalloc byte[16];
        var success = Guid.NewGuid().TryWriteBytes(buffer);
        Debug.Assert(success);
        // 16 * 4/3 = 21.333 which means base64 encoding will use 22 characters of actual data and 2 characters of padding ('=')
        Span<char> base64 = stackalloc char[24];
        success = Convert.TryToBase64Chars(buffer, base64, out var written);
        Debug.Assert(success);
        Debug.Assert(written == 24);
        // Trim the two '=='
        Debug.Assert(base64.EndsWith("=="));
        return new string(base64[..^2]);
    }
}

file class FakeInvocationBinder : IInvocationBinder
{
    public static readonly FakeInvocationBinder Instance = new();

    public IReadOnlyList<Type> GetParameterTypes(string methodName) => throw new NotImplementedException();

    public Type GetReturnType(string invocationId) => typeof(RawResult);

    public Type GetStreamItemType(string streamId) => throw new NotImplementedException();
}
