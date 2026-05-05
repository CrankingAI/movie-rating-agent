namespace MovieRatingAgent.Core.Models;

public record JobMeta
{
    public required string JobId { get; init; }
    public required JobStatus Status { get; init; }
    public required string AgentVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// Per-step progress timeline. Initialized with all expected steps in
    /// <see cref="ProgressState.Pending"/> at job start so the UI can render a
    /// stable checklist; mutated to Running/Completed/Failed as workflow events fire.
    /// </summary>
    public IReadOnlyList<ProgressStep> Progress { get; init; } = [];
}

public record ProgressStep
{
    /// <summary>Stable identifier (e.g. <c>PopularityScorer</c>) used to look up the step.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable label for the UI (e.g. "Scoring popularity").</summary>
    public required string Label { get; init; }

    public required ProgressState State { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public enum ProgressState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}
