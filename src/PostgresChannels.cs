using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace PostgreSignalR;

internal sealed class PostgresChannels(string prefix, string returnServerName)
{
    private const int _maxIdentifierLength = 63;

    public static string Normalize(string prefix, string name)
    {
        Span<byte> utf8 = stackalloc byte[1024];
        var utf8Length = Encoding.UTF8.GetBytes(name.AsSpan(), utf8);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(utf8[..utf8Length], hash);

        Span<byte> base64 = stackalloc byte[44];
        Base64.EncodeToUtf8(hash, base64, out _, out var written);

        return prefix + string.Create(written-1, base64.ToArray(), static (destination, source) =>
        {
            for (int i = 0; i < destination.Length; i++)
            {
                var c = source[i];
                destination[i] = c == (byte)'+' || c == (byte)'/' ? '_' : (char)c;
            }
        });
    }

    /// <summary>
    /// Gets the name of the channel for sending to all connections.
    /// </summary>
    /// <remarks>
    /// The payload on this channel is <see cref="PostgresInvocation"/> objects containing
    /// invocations to be sent to all connections
    /// </remarks>
    public string All { get; } = Normalize(prefix, "all");

    /// <summary>
    /// Gets the name of the internal channel for group management messages.
    /// </summary>
    public string GroupManagement { get; } = Normalize(prefix, "internal_groups");

    /// <summary>
    /// Gets the name of the internal channel for receiving client results.
    /// </summary>
    public string ReturnResults { get; } = Normalize(prefix, $"internal_return_{returnServerName}");

    /// <summary>
    /// Gets the name of the channel for sending a message to a specific connection.
    /// </summary>
    /// <param name="connectionId">The ID of the connection to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Connection(string connectionId) => Normalize(prefix, $"connection_{connectionId}");

    /// <summary>
    /// Gets the name of the channel for sending a message to a named group of connections.
    /// </summary>
    /// <param name="groupName">The name of the group to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Group(string groupName) => Normalize(prefix, $"group_{groupName}");

    /// <summary>
    /// Gets the name of the channel for sending a message to all collections associated with a user.
    /// </summary>
    /// <param name="userId">The ID of the user to get the channel for.</param>32
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string User(string userId) => Normalize(prefix, $"user_{userId}");

    /// <summary>
    /// Gets the name of the acknowledgement channel for the specified server.
    /// </summary>
    /// <param name="serverName">The name of the server to get the acknowledgement channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Ack(string serverName) => Normalize(prefix, $"internal_ack_{serverName}");
}
