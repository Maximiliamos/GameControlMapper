namespace GameControlMapper.Models;

public sealed record TouchShutdownResult(
    bool Succeeded,
    bool FinalFrameAttempted,
    IReadOnlyList<int> ReleasedContactIds,
    IReadOnlyList<int> FailedContactIds);
