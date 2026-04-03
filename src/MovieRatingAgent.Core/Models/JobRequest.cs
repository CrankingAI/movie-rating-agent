namespace MovieRatingAgent.Core.Models;

public record JobRequest
{
    public required string Topic { get; init; }
}
