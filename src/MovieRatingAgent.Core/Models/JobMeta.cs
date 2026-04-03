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
}
