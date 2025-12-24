namespace PostgreSignalR.Benchmarks;

sealed record TrialResult(
    int TargetRateMsgsPerSec,
    int Sent,
    double SendElapsedSec,
    int UniqueReceived,
    int Missing,
    long FanoutCopies,
    long P50Us, long P95Us, long P99Us, long MaxUs
);