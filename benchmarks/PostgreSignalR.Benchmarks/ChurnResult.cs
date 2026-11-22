namespace PostgreSignalR.Benchmarks;

sealed record ChurnResult(
    long Cycles,
    long Failed,
    double ElapsedSec,
    string? FirstError,
    long P50Us, long P95Us, long P99Us, long MaxUs
);
