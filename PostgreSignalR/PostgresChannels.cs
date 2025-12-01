using System.Runtime.CompilerServices;

namespace PostgreSignalR;

internal sealed class PostgresChannels(string prefix, string returnServerName)
{
    /// <summary>
    /// Gets the name of the channel for sending to all connections.
    /// </summary>
    /// <remarks>
    /// The payload on this channel is <see cref="PostgresInvocation"/> objects containing
    /// invocations to be sent to all connections
    /// </remarks>
    public string All { get; } = $"{prefix}_all";

    /// <summary>
    /// Gets the name of the internal channel for group management messages.
    /// </summary>
    public string GroupManagement { get; } = $"{prefix}_internal_groups";

    /// <summary>
    /// Gets the name of the internal channel for receiving client results.
    /// </summary>
    public string ReturnResults { get; } = $"{prefix}_internal_return_{returnServerName.Replace('-', '_')}";

    /// <summary>
    /// Gets the name of the channel for sending a message to a specific connection.
    /// </summary>
    /// <param name="connectionId">The ID of the connection to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Connection(string connectionId) => $"{prefix}_connection_{connectionId.Replace('-', '_')}";

    /// <summary>
    /// Gets the name of the channel for sending a message to a named group of connections.
    /// </summary>
    /// <param name="groupName">The name of the group to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Group(string groupName) => $"{prefix}_group_{groupName.Replace('-', '_')}";

    /// <summary>
    /// Gets the name of the channel for sending a message to all collections associated with a user.
    /// </summary>
    /// <param name="userId">The ID of the user to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string User(string userId) => $"{prefix}_user_{userId.Replace('-', '_')}";

    /// <summary>
    /// Gets the name of the acknowledgement channel for the specified server.
    /// </summary>
    /// <param name="serverName">The name of the server to get the acknowledgement channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Ack(string serverName) => $"{prefix}_internal_ack_{serverName.Replace('-', '_')}";
}
