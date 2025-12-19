using Microsoft.Extensions.Logging;

namespace PostgreSignalR;

internal static partial class LogExtensions
{
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Information,
        Message = "Initializing Postgres backplane..."
    )]
    public static partial void BackplaneInitializing(this ILogger logger);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Initialized Postgres backplane."
    )]
    public static partial void BackplaneInitialized(this ILogger logger);

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Error,
        Message = "Unable to initialize Postgres backplane."
    )]
    public static partial void BackplaneUnableInitialize(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Error,
        Message = "Unable to connect to Postgres backplane."
    )]
    public static partial void BackplaneUnableToConnect(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Information,
        Message = "Postgres backplane unsubscribing from connection {ConnectionId}."
    )]
    public static partial void BackplaneUnsubscribingConnection(this ILogger logger, string connectionId);

    [LoggerMessage(
        EventId = 50,
        Level = LogLevel.Information,
        Message = "Postgres backplane unsubscribing group {Group} from channel {Channel}."
    )]
    public static partial void BackplaneUnsubscribingGroupChannel(this ILogger logger, string group, string channel);

    [LoggerMessage(
        EventId = 60,
        Level = LogLevel.Information,
        Message = "Postgres backplane subscribing user {User} from channel {Channel}."
    )]
    public static partial void BackplaneSubscribingUserChannel(this ILogger logger, string user, string channel);

    [LoggerMessage(
        EventId = 70,
        Level = LogLevel.Information,
        Message = "Postgres backplane unsubscribing user {User} from channel {Channel}."
    )]
    public static partial void BackplaneUnsubscribingUserChannel(this ILogger logger, string user, string channel);

    [LoggerMessage(
        EventId = 80,
        Level = LogLevel.Information,
        Message = "Postgres backplane publishing to channel {Channel}."
    )]
    public static partial void BackplanePublishing(this ILogger logger, string channel);

    [LoggerMessage(
        EventId = 90,
        Level = LogLevel.Error,
        Message = "Postgres backplane failed writing message."
    )]
    public static partial void BackplaneFailedWritingMessage(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Information,
        Message = "Postgres backplane received message from channel {Channel}."
    )]
    public static partial void BackplaneReceivedMessage(this ILogger logger, string channel);

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Information,
        Message = "Postgres backplane read notification on channel {Channel}."
    )]
    public static partial void BackplaneReadNotification(this ILogger logger, string channel);

    [LoggerMessage(
        EventId = 120,
        Level = LogLevel.Warning,
        Message = "Postgres backplane internal message failed."
    )]
    public static partial void BackplaneInternalMessageFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 130,
        Level = LogLevel.Warning,
        Message = "Postgress backplane unable to forward invocation {InvocationId}."
    )]
    public static partial void BackplaneUnableForwardInvocation(this ILogger logger, string? invocationId, Exception exception);

    [LoggerMessage(
        EventId = 140,
        Level = LogLevel.Warning,
        Message = "Postgres backplane encountered server with mismatched protocol; using {Protocol}."
    )]
    public static partial void BackplaneProtocolMismatch(this ILogger logger, string protocol);

    [LoggerMessage(
        EventId = 150,
        Level = LogLevel.Error,
        Message = "Postgres backplane encountered error while parsing result; using protocol {Protocol}."
    )]
    public static partial void BackplaneErrorParsingResult(this ILogger logger, string protocol, Exception? exception = null);

    [LoggerMessage(
        EventId = 160,
        Level = LogLevel.Warning,
        Message = "Postgres backplane encountered error while handling OnInitialized event. Ignoring."
    )]
    public static partial void BackplaneErrorDuringOnInitialized(this ILogger logger, Exception ex);
}
