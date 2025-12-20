using System.Runtime.CompilerServices;

namespace PostgreSignalR;

internal sealed class PostgresChannels(string prefix, string returnServerName)
{
    private const int _maxIdentifierLength = 63;

    private static string Normalize(string name)
    {
        name = name.Replace('-', '_');

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        var suffix = $"_{hash[..8]}";
        var trimLength = Math.Min(Math.Max(_maxIdentifierLength - suffix.Length, 0), name.Length);

        return $"{name[..trimLength]}{suffix}";
    }

    /// <summary>
    /// Gets the name of the channel for sending to all connections.
    /// </summary>
    /// <remarks>
    /// The payload on this channel is <see cref="PostgresInvocation"/> objects containing
    /// invocations to be sent to all connections
    /// </remarks>
    public string All { get; } = Normalize($"{prefix}_all");

    /// <summary>
    /// Gets the name of the internal channel for group management messages.
    /// </summary>
    public string GroupManagement { get; } = Normalize($"{prefix}_internal_groups");

    /// <summary>
    /// Gets the name of the internal channel for receiving client results.
    /// </summary>
    public string ReturnResults { get; } = Normalize($"{prefix}_internal_return_{returnServerName}");

    /// <summary>
    /// Gets the name of the channel for sending a message to a specific connection.
    /// </summary>
    /// <param name="connectionId">The ID of the connection to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Connection(string connectionId) => Normalize($"{prefix}_connection_{connectionId}");

    /// <summary>
    /// Gets the name of the channel for sending a message to a named group of connections.
    /// </summary>
    /// <param name="groupName">The name of the group to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Group(string groupName) => Normalize($"{prefix}_group_{groupName}");

    /// <summary>
    /// Gets the name of the channel for sending a message to all collections associated with a user.
    /// </summary>
    /// <param name="userId">The ID of the user to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string User(string userId) => Normalize($"{prefix}_user_{userId}");

    /// <summary>
    /// Gets the name of the acknowledgement channel for the specified server.
    /// </summary>
    /// <param name="serverName">The name of the server to get the acknowledgement channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Ack(string serverName) => Normalize($"{prefix}_internal_ack_{serverName}");
}
