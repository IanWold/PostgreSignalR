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
    public HashSet<string> Groups { get; } = [with(StringComparer.OrdinalIgnoreCase)];
}

file class FakeInvocationBinder : IInvocationBinder
{
    public static readonly FakeInvocationBinder Instance = new();

    public IReadOnlyList<Type> GetParameterTypes(string methodName) => throw new NotImplementedException();

    public Type GetReturnType(string invocationId) => typeof(RawResult);

    public Type GetStreamItemType(string streamId) => throw new NotImplementedException();
}

public sealed class PostgresHubLifetimeManager<THub> : HubLifetimeManager<THub>, IDisposable where THub : Hub
{
    private readonly HubConnectionStore _connections = new();
    private readonly PostgresSubscriptionManager _groups = new();
    private readonly PostgresSubscriptionManager _users = new();
    private readonly AckHandler _ackHandler;
    private readonly ChannelNameProvider _channels;
    private readonly IHubProtocolResolver _hubProtocolResolver;
    private readonly ILogger<PostgresHubLifetimeManager<THub>> _logger;
    private readonly PostgresBackplaneOptions _options;
    private readonly string _serverName = GenerateServerName();
    private readonly PostgresProtocol _protocol;
    private readonly SemaphoreSlim _commandLock = new(1);
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Action<byte[]>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly ClientResultsManager _clientResultsManager = new();
    private readonly PostgresListener _postgresListener;
    private readonly IPayloadStrategy _payloadStrategy;
    private readonly IPrefixedPayloadStrategy? _prefixedPayloadStrategy;
    private int _internalAckId;
    private bool _isInitialized;

    public PostgresHubLifetimeManager(
        IPayloadStrategy payloadStrategy,
        IHubProtocolResolver hubProtocolResolver,
        IOptions<PostgresBackplaneOptions> options,
        IOptions<HubOptions>? globalHubOptions,
        IOptions<HubOptions<THub>>? hubOptions,
        ILogger<PostgresHubLifetimeManager<THub>> logger
    )
    {
        _options = options.Value;

        if (!_options.IsValid(out var validationMessage))
        {
            logger.BackplaneConfigurationInvalid(validationMessage!);
            throw new Exception(validationMessage);
        }

        _hubProtocolResolver = hubProtocolResolver;
        _logger = logger;
        _ackHandler = new AckHandler();

        var prefix = string.IsNullOrEmpty(_options.Prefix)
            ? string.Empty
            : $"{_options.Prefix.TrimEnd("_")}_";

        var hubScope = _options.ChannelRouting == ChannelRouting.Shared
            ? ChannelNameProvider.GetHubScope(typeof(THub))
            : null;

        _channels = _options.ChannelNameNormaization switch
        {
            ChannelNameNormaization.Truncate => new TruncatingChannelNameProvider(prefix, _serverName, hubScope),
            ChannelNameNormaization.HashAlways or _ => new HashingChannelNameProvider(prefix, _serverName, hubScope)
        };

        if (globalHubOptions != null && hubOptions != null)
        {
            _protocol = new PostgresProtocol(hubProtocolResolver, globalHubOptions.Value.SupportedProtocols, hubOptions.Value.SupportedProtocols);
        }
        else
        {
            var supportedProtocols = hubProtocolResolver.AllProtocols.Select(p => p.Name).ToList();
            _protocol = new PostgresProtocol(hubProtocolResolver, supportedProtocols, null);
        }

        _payloadStrategy = payloadStrategy;
        _prefixedPayloadStrategy = payloadStrategy as IPrefixedPayloadStrategy;

        _postgresListener = new(options.Value.DataSource, logger);
        _postgresListener.OnNotification += OnNotification;
        _postgresListener.OnReconnected += OnReconnected;
    }

    /// <summary>
    /// The number of channels this hub's listener connection is subscribed to. For diagnostics and tests.
    /// </summary>
    internal int ListenerChannelCount => _postgresListener.ChannelCount;

    /// <summary>
    /// The number of LISTEN statements this hub's listener connection has run. For diagnostics and tests.
    /// </summary>
    internal long ListenerListenCount => _postgresListener.ListenCount;

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync();

        try
        {
            if (_isInitialized)
            {
                return;
            }

            _logger.BackplaneInitializing();

            try
            {
                await _postgresListener.EnsureReadyAsync();

                var subscriptions = new List<Task>
                {
                    SubscribeToAll(),
                    SubscribeToGroupManagementChannel(),
                    SubscribeToAckChannel(),
                    SubscribeToReturnResultsAsync()
                };

                if (_options.ChannelRouting == ChannelRouting.Shared)
                {
                    subscriptions.Add(SubscribeToSharedChannelAsync());
                }

                await Task.WhenAll(subscriptions);

                _isInitialized = true;

                try
                {
                    _options.InvokeOnInitialized();
                }
                catch (Exception ex)
                {
                    _logger.BackplaneErrorDuringOnInitialized(ex);
                }

                _logger.BackplaneInitialized();
            }
            catch (Exception ex)
            {
                _logger.BackplaneUnableInitialize(ex);
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public override async Task OnConnectedAsync(HubConnectionContext connection)
    {
        await EnsureInitializedAsync();

        var feature = new PostgresFeature();
        connection.Features.Set(feature);

        _connections.Add(connection);

        if (_options.ChannelRouting == ChannelRouting.Shared)
        {
            if (!string.IsNullOrEmpty(connection.UserIdentifier))
            {
                await _users.AddSubscriptionAsync(connection.UserIdentifier, connection, (_, _) => Task.CompletedTask);
            }

            return;
        }

        var userTask = Task.CompletedTask;

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

        var tasks = new List<Task>();

        if (_options.ChannelRouting != ChannelRouting.Shared)
        {
            _logger.BackplaneUnsubscribingConnection(connection.ConnectionId);
            tasks.Add(UnsubscribeAsync(_channels.Connection(connection.ConnectionId)));
        }

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
        await PublishToTargetAsync(RoutingTarget.Connection, connectionId, message);
    }

    /// <inheritdoc />
    public override async Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(groupName);

        var message = _protocol.WriteInvocation(methodName, args);
        await PublishToTargetAsync(RoutingTarget.Group, groupName, message);
    }

    /// <inheritdoc />
    public override async Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        ArgumentNullException.ThrowIfNull(groupName);

        var message = _protocol.WriteInvocation(methodName, args, excludedConnectionIds: excludedConnectionIds);
        await PublishToTargetAsync(RoutingTarget.Group, groupName, message);
    }

    /// <inheritdoc />
    public override async Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var message = _protocol.WriteInvocation(methodName, args);
        await PublishToTargetAsync(RoutingTarget.User, userId, message);
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
            publishTasks.Add(PublishToTargetAsync(RoutingTarget.Connection, connectionId, payload));
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
                publishTasks.Add(PublishToTargetAsync(RoutingTarget.Group, groupName, payload));
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
                    publishTasks.Add(PublishToTargetAsync(RoutingTarget.User, userId, payload));
                }
            }

            await Task.WhenAll(publishTasks);
            return;
        }
    }

    private Task PublishToTargetAsync(RoutingTarget target, string id, byte[] message)
    {
        if (_options.ChannelRouting != ChannelRouting.Shared)
        {
            var channel = target switch
            {
                RoutingTarget.Connection => _channels.Connection(id),
                RoutingTarget.Group => _channels.Group(id),
                RoutingTarget.User => _channels.User(id),
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };

            return PublishAsync(channel, message);
        }

        if (_prefixedPayloadStrategy is not null)
        {
            return PublishAsync(_channels.Shared, message, PostgresProtocol.WriteRoutingHeader(target, id));
        }

        return PublishAsync(_channels.Shared, PostgresProtocol.WriteRoutedMessage(target, id, message));
    }

    private async Task PublishAsync(string channel, byte[] message, string? routingHeader = null)
    {
        _logger.BackplanePublishing(channel);

        await _commandLock.WaitAsync();
        try
        {
            if (routingHeader is null)
            {
                await _payloadStrategy.NotifyAsync(channel, message);
            }
            else
            {
                await _prefixedPayloadStrategy!.NotifyAsync(channel, routingHeader, message);
            }
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

        if (_options.ChannelRouting == ChannelRouting.Shared)
        {
            return _groups.AddSubscriptionAsync(groupName, connection, (_, _) => Task.CompletedTask);
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
        if (_options.ChannelRouting == ChannelRouting.Shared)
        {
            await _groups.RemoveSubscriptionAsync(groupName, connection, this, (_, _) => Task.CompletedTask);
        }
        else
        {
            var groupChannel = _channels.Group(groupName);

            await _groups.RemoveSubscriptionAsync(groupChannel, connection, this, (state, channelName) =>
            {
                var lifetimeManager = (PostgresHubLifetimeManager<THub>)state;
                lifetimeManager._logger.BackplaneUnsubscribingGroupChannel(groupChannel, channelName);
                return lifetimeManager.UnsubscribeAsync(channelName);
            });
        }

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
        if (_options.ChannelRouting == ChannelRouting.Shared)
        {
            return _users.RemoveSubscriptionAsync(connection.UserIdentifier!, connection, this, (_, _) => Task.CompletedTask);
        }

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
                await PublishToTargetAsync(RoutingTarget.Connection, connectionId, messageBytes);
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

            await WriteToConnectionsAsync(_connections, invocation);
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
        try
        {
            await WriteToConnectionAsync(connection, PostgresProtocol.ReadInvocation(message));
        }
        catch (Exception ex)
        {
            _logger.BackplaneFailedWritingMessage(ex);
        }
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
                    await WriteToConnectionsAsync(subscriptions, PostgresProtocol.ReadInvocation(message));
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
            await WriteToConnectionsAsync(groupConnections, PostgresProtocol.ReadInvocation(message));
        }
        catch (Exception ex)
        {
            _logger.BackplaneFailedWritingMessage(ex);
        }
    });

    private async Task SubscribeToSharedChannelAsync()
    {
        // Notifications on the shared channel are handled by OnSharedNotification,
        // because they must be routed before the payload is resolved, so this doesn't register a handler.
        await _postgresListener.SubscribeAsync(_channels.Shared);
    }

    private async Task WriteToConnectionAsync(HubConnectionContext connection, PostgresInvocation invocation)
    {
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
                    connection.Protocol.WriteMessage(completionMessage, memoryBufferWriter);
                    message = PostgresProtocol.WriteCompletionMessage(memoryBufferWriter, protocolName);

                    await PublishAsync(invocation.ReturnChannel!, message);
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
    }

    private static Task WriteToConnectionsAsync(HubConnectionStore connections, PostgresInvocation invocation)
    {
        var tasks = new List<Task>(connections.Count);

        foreach (var connection in connections)
        {
            if (invocation.ExcludedConnectionIds?.Contains(connection.ConnectionId) == true)
            {
                continue;
            }

            tasks.Add(connection.WriteAsync(invocation.Message).AsTask());
        }

        return Task.WhenAll(tasks);
    }

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

    private void OnReconnected(object? sender, EventArgs e)
    {
        try
        {
            _options.InvokeOnReconnected();
        }
        catch (Exception ex)
        {
            _logger.BackplaneErrorDuringOnReconnected(ex);
        }
    }

    private void OnNotification(object? sender, NpgsqlNotificationEventArgs e)
    {
        // Exceptions must not escape: this runs inside the listener's wait for notifications, and an exception there makes the listener reconnect.

        _logger.BackplaneReadNotification(e.Channel);

        if (_options.ChannelRouting == ChannelRouting.Shared && string.Equals(e.Channel, _channels.Shared, StringComparison.Ordinal))
        {
            OnSharedNotification(e);
            return;
        }

        if (_notificationHandlers.TryGetValue(e.Channel, out var handler))
        {
            byte[] payload;
            try
            {
                payload = _payloadStrategy.ResolveNotificationPayload(e);
            }
            catch (Exception ex)
            {
                _logger.BackplaneFailedResolvingPayload(e.Channel, ex);
                return;
            }

            try
            {
                handler(payload);
            }
            catch (Exception ex)
            {
                _logger.BackplaneFailedHandlingNotification(e.Channel, ex);
            }
        }
    }

    private void OnSharedNotification(NpgsqlNotificationEventArgs e)
    {
        RoutingHeader header;
        ReadOnlyMemory<byte> message;

        try
        {
            if (_prefixedPayloadStrategy is not null)
            {
                var payloadStart = PostgresProtocol.ReadRoutingHeader(e.Payload, out header);

                if (!HasLocalTarget(header))
                {
                    _logger.BackplaneSkippedSharedMessage(header.RoutingTarget.ToString());
                    return;
                }

                message = _prefixedPayloadStrategy.ResolveNotificationPayload(e.Payload[payloadStart..]);
            }
            else
            {
                message = PostgresProtocol.ReadRoutedMessage(_payloadStrategy.ResolveNotificationPayload(e), out header);

                if (!HasLocalTarget(header))
                {
                    _logger.BackplaneSkippedSharedMessage(header.RoutingTarget.ToString());
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.BackplaneFailedResolvingPayload(e.Channel, ex);
            return;
        }

        _ = DeliverSharedMessageAsync(header, message);
    }

    private bool HasLocalTarget(RoutingHeader header)
    {
        foreach (var id in header.Ids)
        {
            var isLocal = header.RoutingTarget switch
            {
                RoutingTarget.Connection => _connections[id] is not null,
                RoutingTarget.User => _users.TryGetConnections(id, out _),
                RoutingTarget.Group => _groups.TryGetConnections(id, out _),
                _ => false
            };

            if (isLocal)
            {
                return true;
            }
        }

        return false;
    }

    private async Task DeliverSharedMessageAsync(RoutingHeader header, ReadOnlyMemory<byte> message)
    {
        try
        {
            _logger.BackplaneReceivedMessage(_channels.Shared);

            var invocation = PostgresProtocol.ReadInvocation(message);

            foreach (var id in header.Ids)
            {
                switch (header.RoutingTarget)
                {
                    case RoutingTarget.Connection when _connections[id] is { } connection:
                        await WriteToConnectionAsync(connection, invocation);
                        break;

                    case RoutingTarget.User when _users.TryGetConnections(id, out var userConnections):
                        await WriteToConnectionsAsync(userConnections, invocation);
                        break;

                    case RoutingTarget.Group when _groups.TryGetConnections(id, out var groupConnections):
                        await WriteToConnectionsAsync(groupConnections, invocation);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.BackplaneFailedWritingMessage(ex);
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
