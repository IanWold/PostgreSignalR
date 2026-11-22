/// <param name="Target"><c>all</c> (default), <c>connection</c>, <c>user</c> or <c>group</c>.</param>
/// <param name="Targets">Connection ids, users or groups to send to. Message <c>i</c> goes to <c>Targets[i % Targets.Length]</c>.</param>
public sealed record PublishRequest(
    int PublishCount,
    int Concurrency,
    int PayloadBytes,
    string? Target = null,
    string[]? Targets = null
);
