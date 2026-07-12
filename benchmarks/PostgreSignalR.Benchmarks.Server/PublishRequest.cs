namespace PostgreSignalR.Benchmarks.Server;

public sealed record PublishRequest(
    int PublishCount,
    int Concurrency,
    int PayloadBytes,
    long Generation
);
