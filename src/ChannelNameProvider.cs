using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace PostgreSignalR;

internal abstract class ChannelNameProvider
{
    public ChannelNameProvider(string returnServerName)
    {
        All = Normalize("all");
        GroupManagement = Normalize("grp_mgm");
        ReturnResults = Normalize($"ret_{returnServerName}");
    }

    /// <summary>
    /// Postgres limits channel names to identifiers up to 63 characters.
    /// Normalization should ensure any channel names meet this.
    /// </summary>
    /// <param name="name">The raw name of the channel</param>
    /// <returns>The normalized channel name</returns>
    internal abstract string Normalize(string name);

    /// <summary>
    /// Gets the name of the channel for sending to all connections.
    /// </summary>
    /// <remarks>
    /// The payload on this channel is <see cref="PostgresInvocation"/> objects containing
    /// invocations to be sent to all connections
    /// </remarks>
    public string All { get; init; } 

    /// <summary>
    /// Gets the name of the internal channel for group management messages.
    /// </summary>
    public string GroupManagement { get; }

    /// <summary>
    /// Gets the name of the internal channel for receiving client results.
    /// </summary>
    public string ReturnResults { get; }

    /// <summary>
    /// Gets the name of the channel for sending a message to a specific connection.
    /// </summary>
    /// <param name="connectionId">The ID of the connection to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Connection(string connectionId) => Normalize($"con_{connectionId}");

    /// <summary>
    /// Gets the name of the channel for sending a message to a named group of connections.
    /// </summary>
    /// <param name="groupName">The name of the group to get the channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Group(string groupName) => Normalize($"grp_{groupName}");

    /// <summary>
    /// Gets the name of the channel for sending a message to all collections associated with a user.
    /// </summary>
    /// <param name="userId">The ID of the user to get the channel for.</param>32
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string User(string userId) => Normalize($"usr_{userId}");

    /// <summary>
    /// Gets the name of the acknowledgement channel for the specified server.
    /// </summary>
    /// <param name="serverName">The name of the server to get the acknowledgement channel for.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Ack(string serverName) => Normalize($"ack_{serverName}");
}

internal sealed class TruncatingChannelNameProvider(string prefix, string returnServerName) : ChannelNameProvider(returnServerName)
{
    internal override string Normalize(string name)
    {
        name = prefix + name;

        if (name.Length <= 63)
        {
            return name;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        var suffix = $"_{hash[..8]}";
        var trimLength = Math.Max(63 - suffix.Length, 0);

        return $"{name[..trimLength]}{suffix}";
    }
}

internal sealed class HashingChannelNameProvider(string prefix, string returnServerName) : ChannelNameProvider(returnServerName)
{
    // Below this many UTF-8 bytes we encode on the stack;
    // names larger than this are rented from the array pool to never risk overflowing a fixed-size stack buffer
    private const int StackallocThreshold = 256;

    internal override string Normalize(string name)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(name.Length);
        var rented = maxByteCount > StackallocThreshold
            ? ArrayPool<byte>.Shared.Rent(maxByteCount)
            : null;

        try
        {
            Span<byte> utf8 = rented is not null
                ? rented
                : stackalloc byte[StackallocThreshold];
            
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
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
