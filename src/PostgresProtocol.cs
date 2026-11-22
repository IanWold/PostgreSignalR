using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MessagePack;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace PostgreSignalR;

internal readonly struct PostgresInvocation(SerializedHubMessage message, IReadOnlyList<string>? excludedConnectionIds, string? invocationId = null, string? returnChannel = null)
{
    /// <summary>
    /// Gets a list of connections that should be excluded from this invocation.
    /// May be null to indicate that no connections are to be excluded.
    /// </summary>
    public IReadOnlyList<string>? ExcludedConnectionIds { get; } = excludedConnectionIds;

    /// <summary>
    /// Gets the message serialization cache containing serialized payloads for the message.
    /// </summary>
    public SerializedHubMessage Message { get; } = message;

    public string? ReturnChannel { get; } = returnChannel;

    public string? InvocationId { get; } = invocationId;
}

internal record struct PostgresGroupCommand(int Id, string ServerName, GroupAction Action, string GroupName, string ConnectionId);

internal enum GroupAction : byte
{
    // These numbers are used by the protocol, do not change them and always use explicit assignment
    // when adding new items to this enum. 0 is intentionally omitted
    Add = 1,
    Remove = 2,
}

internal record struct PostgresCompletion(string ProtocolName, ReadOnlySequence<byte> CompletionMessage);

internal enum RoutingTarget : byte
{
    // These numbers are used by the protocol, do not change them and always use explicit assignment when adding new items to this enum.
    // 0 is intentionally omitted
    Connection = 1,
    Group = 2,
    User = 3,
}

internal readonly record struct RoutingHeader(RoutingTarget RoutingTarget, IReadOnlyList<string> Ids);

internal sealed class PostgresProtocol
{
    private readonly List<IHubProtocol> _hubProtocols;

    public PostgresProtocol(IHubProtocolResolver hubProtocolResolver, IList<string>? globalSupportedProtocols, IList<string>? hubSupportedProtocols)
    {
        var supportedProtocols = hubSupportedProtocols ?? globalSupportedProtocols ?? Array.Empty<string>();
        _hubProtocols = [..
            supportedProtocols
            .Select(p => hubProtocolResolver.GetProtocol(p, (supportedProtocols as IReadOnlyList<string>) ?? [.. supportedProtocols]))
            .Where(p => p is not null)
            .Cast<IHubProtocol>()
        ];
    }

    private List<SerializedMessage> SerializeMessage(HubMessage message)
    {
        var list = new List<SerializedMessage>(_hubProtocols.Count);
        foreach (var protocol in _hubProtocols)
        {
            list.Add(new SerializedMessage(protocol.Name, protocol.GetMessageBytes(message)));
        }

        return list;
    }

    // The Postgres Protocol:
    // * The message type is known in advance because messages are sent to different channels based on type
    // * Invocations are sent to the All, Group, Connection and User channels
    // * Under shared channel routing, invocations for connections, users and groups are sent to the Shared channel
    //   instead, with a routing header saying which targets they're for. See WriteRoutingHeader.
    // * Group Commands are sent to the GroupManagement channel
    // * Acks are sent to the Acknowledgement channel.
    // * Completion messages (client results) are sent to the server specific Result channel
    // * See the Write[type] methods for a description of the protocol for each in-depth.
    // * The "Variable length integer" is the length-prefixing format used by BinaryReader/BinaryWriter:
    //   * https://learn.microsoft.com/dotnet/api/system.io.binarywriter.write?view=netcore-2.2
    // * The "Length prefixed string" is the string format used by BinaryReader/BinaryWriter:
    //   * A 7-bit variable length integer encodes the length in bytes, followed by the encoded string in UTF-8.

    public byte[] WriteInvocation(string methodName, object?[] args, string? invocationId = null,
        IReadOnlyList<string>? excludedConnectionIds = null, string? returnChannel = null)
    {
        // Written as a MessagePack 'arr' containing at least these items:
        // * A MessagePack 'arr' of 'str's representing the excluded ids
        // * [The output of WriteSerializedHubMessage, which is an 'arr']
        // For invocations expecting a result
        // * InvocationID
        // * Postgres return channel
        // Any additional items are discarded.

        var memoryBufferWriter = MemoryBufferWriter.Get();
        try
        {
            var writer = new MessagePackWriter(memoryBufferWriter);

            if (!string.IsNullOrEmpty(returnChannel))
            {
                writer.WriteArrayHeader(4);
            }
            else
            {
                writer.WriteArrayHeader(2);
            }
            if (excludedConnectionIds != null && excludedConnectionIds.Count > 0)
            {
                writer.WriteArrayHeader(excludedConnectionIds.Count);
                foreach (var id in excludedConnectionIds)
                {
                    writer.Write(id);
                }
            }
            else
            {
                writer.WriteArrayHeader(0);
            }

            WriteHubMessage(ref writer, new InvocationMessage(invocationId, methodName, args));

            // Write last in order to preserve original order for cases where one server is updated and the other isn't.
            // Not really a supported scenario, but why not be nice
            if (!string.IsNullOrEmpty(returnChannel))
            {
                writer.Write(invocationId);
                writer.Write(returnChannel);
            }

            writer.Flush();

            return memoryBufferWriter.ToArray();
        }
        finally
        {
            MemoryBufferWriter.Return(memoryBufferWriter);
        }
    }

    public static byte[] WriteGroupCommand(PostgresGroupCommand command)
    {
        // Written as a MessagePack 'arr' containing at least these items:
        // * An 'int': the Id of the command
        // * A 'str': The server name
        // * An 'int': The action (likely less than 0x7F and thus a single-byte fixnum)
        // * A 'str': The group name
        // * A 'str': The connection Id
        // Any additional items are discarded.

        var memoryBufferWriter = MemoryBufferWriter.Get();
        try
        {
            var writer = new MessagePackWriter(memoryBufferWriter);

            writer.WriteArrayHeader(5);
            writer.Write(command.Id);
            writer.Write(command.ServerName);
            writer.Write((byte)command.Action);
            writer.Write(command.GroupName);
            writer.Write(command.ConnectionId);
            writer.Flush();

            return memoryBufferWriter.ToArray();
        }
        finally
        {
            MemoryBufferWriter.Return(memoryBufferWriter);
        }
    }

    public static byte[] WriteAck(int messageId)
    {
        // Written as a MessagePack 'arr' containing at least these items:
        // * An 'int': The Id of the command being acknowledged.
        // Any additional items are discarded.

        var memoryBufferWriter = MemoryBufferWriter.Get();
        try
        {
            var writer = new MessagePackWriter(memoryBufferWriter);

            writer.WriteArrayHeader(1);
            writer.Write(messageId);
            writer.Flush();

            return memoryBufferWriter.ToArray();
        }
        finally
        {
            MemoryBufferWriter.Return(memoryBufferWriter);
        }
    }

    public static byte[] WriteCompletionMessage(MemoryBufferWriter writer, string protocolName)
    {
        // Written as a MessagePack 'arr' containing at least these items:
        // * A 'str': The name of the HubProtocol used for the serialization of the Completion Message
        // * [A serialized Completion Message which is a 'bin']
        // Any additional items are discarded.

        using var completionMessage = writer.DetachAndReset();
        var msgPackWriter = new MessagePackWriter(writer);

        msgPackWriter.WriteArrayHeader(2);
        msgPackWriter.Write(protocolName);

        msgPackWriter.WriteBinHeader(completionMessage.ByteLength);
        foreach (var segment in completionMessage.Segments)
        {
            msgPackWriter.WriteRaw(segment.Span);
        }
        completionMessage.Dispose();

        msgPackWriter.Flush();
        return writer.ToArray();
    }

    /// <summary>
    /// Separates the routing header from the payload strategy's own text in a notification payload.
    /// It is not in the base64 alphabet, so it can't appear in the header.
    /// </summary>
    public const char RoutingHeaderSeparator = '.';

    public static string WriteRoutingHeader(RoutingTarget target, string id)
    {
        // Written as the base64 encoding of the routing header (see WriteRoutingHeader(ref MessagePackWriter...)),
        // followed by RoutingHeaderSeparator. Payload strategies that support routing headers put this text
        // in front of their own text in the notification payload, so a receiver can read the header without
        // resolving the payload. Base64 keeps the text ASCII, so its length in characters is its length in bytes.

        var memoryBufferWriter = MemoryBufferWriter.Get();
        try
        {
            var writer = new MessagePackWriter(memoryBufferWriter);
            WriteRoutingHeader(ref writer, target, id);
            writer.Flush();

            return Convert.ToBase64String(memoryBufferWriter.ToArray()) + RoutingHeaderSeparator;
        }
        finally
        {
            MemoryBufferWriter.Return(memoryBufferWriter);
        }
    }

    /// <summary>
    /// Reads the routing header from the front of a notification payload.
    /// </summary>
    /// <returns>The index in <paramref name="notificationPayload"/> where the payload strategy's own text starts.</returns>
    public static int ReadRoutingHeader(string notificationPayload, out RoutingHeader header)
    {
        // See WriteRoutingHeader(RoutingTargetKind, string) for the format
        var separator = notificationPayload.IndexOf(RoutingHeaderSeparator);
        if (separator < 0)
        {
            throw new InvalidDataException("Notification payload has no routing header.");
        }

        var reader = new MessagePackReader(Convert.FromBase64String(notificationPayload[..separator]));
        header = ReadRoutingHeader(ref reader);

        return separator + 1;
    }

    public static byte[] WriteRoutedMessage(RoutingTarget target, string id, byte[] message)
    {
        // Used when the payload strategy doesn't support routing headers, so the header travels inside the message.
        // Written as a MessagePack 'arr' containing at least these items:
        // * The routing header (see WriteRoutingHeader(ref MessagePackWriter...)), which is an 'arr'
        // * A 'bin': the message, as written by WriteInvocation
        // Any additional items are discarded.

        var memoryBufferWriter = MemoryBufferWriter.Get();
        try
        {
            var writer = new MessagePackWriter(memoryBufferWriter);

            writer.WriteArrayHeader(2);
            WriteRoutingHeader(ref writer, target, id);
            writer.Write(message);
            writer.Flush();

            return memoryBufferWriter.ToArray();
        }
        finally
        {
            MemoryBufferWriter.Return(memoryBufferWriter);
        }
    }

    /// <summary>
    /// Reads a message written by <see cref="WriteRoutedMessage"/>.
    /// </summary>
    /// <returns>The message, as written by <see cref="WriteInvocation"/>.</returns>
    public static ReadOnlyMemory<byte> ReadRoutedMessage(ReadOnlyMemory<byte> data, out RoutingHeader header)
    {
        // See WriteRoutedMessage for the format
        var reader = new MessagePackReader(data);
        var length = ValidateArraySize(ref reader, 2, "RoutedMessage");

        header = ReadRoutingHeader(ref reader);

        var message = reader.ReadBytes() ?? throw new InvalidDataException("RoutedMessage has no message.");

        SkipRemaining(ref reader, length - 2);

        return message.IsSingleSegment ? message.First : message.ToArray();
    }

    private static void WriteRoutingHeader(ref MessagePackWriter writer, RoutingTarget target, string id)
    {
        // Written as a MessagePack 'arr' containing at least these items:
        // * An 'int': the target kind (a single-byte fixnum)
        // * A MessagePack 'arr' of 'str's: the target ids. The connection ID, user ID or group name, unnormalized.
        // Any additional items are discarded.

        writer.WriteArrayHeader(2);
        writer.Write((byte)target);
        writer.WriteArrayHeader(1);
        writer.Write(id ?? string.Empty);
    }

    private static RoutingHeader ReadRoutingHeader(ref MessagePackReader reader)
    {
        var length = ValidateArraySize(ref reader, 2, "RoutingHeader");

        var target = (RoutingTarget)reader.ReadByte();

        var idCount = reader.ReadArrayHeader();
        var ids = new string[idCount];
        for (var i = 0; i < idCount; i++)
        {
            ids[i] = reader.ReadString() ?? string.Empty;
        }

        SkipRemaining(ref reader, length - 2);

        return new RoutingHeader(target, ids);
    }

    private static void SkipRemaining(ref MessagePackReader reader, int count)
    {
        for (var i = 0; i < count; i++)
        {
            reader.Skip();
        }
    }

    public static PostgresInvocation ReadInvocation(ReadOnlyMemory<byte> data)
    {
        // See WriteInvocation for the format
        var reader = new MessagePackReader(data);
        var length = ValidateArraySize(ref reader, 2, "Invocation");

        string? returnChannel = null;
        string? invocationId = null;

        // Read excluded Ids
        IReadOnlyList<string>? excludedConnectionIds = null;
        var idCount = reader.ReadArrayHeader();
        if (idCount > 0)
        {
            var ids = new string[idCount];
            for (var i = 0; i < idCount; i++)
            {
                ids[i] = reader.ReadString()!;
            }

            excludedConnectionIds = ids;
        }

        // Read payload
        var message = ReadSerializedHubMessage(ref reader);

        if (length > 3)
        {
            invocationId = reader.ReadString();
            returnChannel = reader.ReadString();
        }

        return new PostgresInvocation(message, excludedConnectionIds, invocationId, returnChannel);
    }

    public static PostgresGroupCommand ReadGroupCommand(ReadOnlyMemory<byte> data)
    {
        var reader = new MessagePackReader(data);

        // See WriteGroupCommand for format.
        ValidateArraySize(ref reader, 5, "GroupCommand");

        var id = reader.ReadInt32();
        var serverName = reader.ReadString()!;
        var action = (GroupAction)reader.ReadByte();
        var groupName = reader.ReadString()!;
        var connectionId = reader.ReadString()!;

        return new PostgresGroupCommand(id, serverName, action, groupName, connectionId);
    }

    public static int ReadAck(ReadOnlyMemory<byte> data)
    {
        var reader = new MessagePackReader(data);

        // See WriteAck for format
        ValidateArraySize(ref reader, 1, "Ack");
        return reader.ReadInt32();
    }

    private void WriteHubMessage(ref MessagePackWriter writer, HubMessage message)
    {
        // Written as a MessagePack 'map' where the keys are the name of the protocol (as a MessagePack 'str')
        // and the values are the serialized blob (as a MessagePack 'bin').

        var serializedHubMessages = SerializeMessage(message);

        writer.WriteMapHeader(serializedHubMessages.Count);

        foreach (var serializedMessage in serializedHubMessages)
        {
            writer.Write(serializedMessage.ProtocolName);

            var isArray = MemoryMarshal.TryGetArray(serializedMessage.Serialized, out var array);
            Debug.Assert(isArray);
            writer.Write(array);
        }
    }

    public static SerializedHubMessage ReadSerializedHubMessage(ref MessagePackReader reader)
    {
        var count = reader.ReadMapHeader();
        var serializations = new SerializedMessage[count];
        for (var i = 0; i < count; i++)
        {
            var protocol = reader.ReadString()!;
            var serialized = reader.ReadBytes()?.ToArray() ?? Array.Empty<byte>();

            serializations[i] = new SerializedMessage(protocol, serialized);
        }

        return new SerializedHubMessage(serializations);
    }

    public static PostgresCompletion ReadCompletion(ReadOnlyMemory<byte> data)
    {
        // See WriteCompletionMessage for the format
        var reader = new MessagePackReader(data);
        ValidateArraySize(ref reader, 2, "CompletionMessage");

        var protocolName = reader.ReadString()!;
        var ros = reader.ReadBytes();
        return new PostgresCompletion(protocolName, ros ?? new ReadOnlySequence<byte>());
    }

    private static int ValidateArraySize(ref MessagePackReader reader, int expectedLength, string messageType)
    {
        var length = reader.ReadArrayHeader();

        if (length < expectedLength)
        {
            throw new InvalidDataException($"Insufficient items in {messageType} array.");
        }
        return length;
    }
}
