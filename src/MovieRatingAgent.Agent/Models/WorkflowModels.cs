namespace MovieRatingAgent.Agent.Models;

public record ScorerResult
{
    public required string Category { get; init; }
    public required int Score { get; init; }
    public required IReadOnlyList<string> Pros { get; init; }
    public required IReadOnlyList<string> Cons { get; init; }
}

public record ScorerResultSet
{
    public required IReadOnlyList<ScorerResult> Results { get; init; }
}

public record ProsConsRollupResult
{
    public required IReadOnlyList<string> Pros { get; init; }
    public required IReadOnlyList<string> Cons { get; init; }
    public required IReadOnlyList<string> Conflicts { get; init; }
}

public record WorkflowResult
{
    public required int Score { get; init; }
    public required ProsConsRollupResult ProsConsRollup { get; init; }
    public required SubScoreSet SubScores { get; init; }
}

public record SubScoreSet
{
    public required int Popularity { get; init; }
    public required int ArtisticValue { get; init; }
    public required int Iconicness { get; init; }
}

public record RollupInput
{
    public required ScorerResultSet ScorerResults { get; init; }
    public required ProsConsRollupResult ProsConsRollup { get; init; }
}
