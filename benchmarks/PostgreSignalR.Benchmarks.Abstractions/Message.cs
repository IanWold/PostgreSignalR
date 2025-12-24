namespace PostgreSignalR.Benchmarks.Abstractions;

public record Message(
    string MessageId,
    long SentUnixTimeMs,
    int PayloadBytes
);
