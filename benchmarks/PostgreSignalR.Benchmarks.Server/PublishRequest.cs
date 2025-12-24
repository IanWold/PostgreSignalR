public sealed record PublishRequest(
    int PublishCount,
    int Concurrency,
    int PayloadBytes
);
