namespace MovieRatingAgent.Agent.Progress;

/// <summary>
/// Canonical list of progress step names + labels. The runtime path (the
/// agent + sink) and the seed path (RunJobFunction populating the initial
/// meta.json) both consume this so the UI checklist is always consistent
/// with what the workflow actually emits.
/// </summary>
public static class ProgressSteps
{
    public const string TitleResolution = "TitleResolution";
    public const string PopularityScorer = "PopularityScorer";
    public const string ArtisticValueScorer = "ArtisticValueScorer";
    public const string IconicnessScorer = "IconicnessScorer";
    public const string ProsConsRollup = "ProsConsRollup";
    public const string WeightedScoreRollup = "WeightedScoreRollup";

    /// <summary>Steps in the order they are presented to the user.</summary>
    public static readonly IReadOnlyList<(string Name, string Label)> Ordered =
    [
        (TitleResolution, "Resolving movie title"),
        (PopularityScorer, "Scoring popularity"),
        (ArtisticValueScorer, "Scoring artistic value"),
        (IconicnessScorer, "Scoring iconicness"),
        (ProsConsRollup, "Merging pros & cons"),
        (WeightedScoreRollup, "Computing weighted score"),
    ];

    /// <summary>Workflow executor IDs that should NOT appear in the user-visible checklist.</summary>
    public static readonly IReadOnlySet<string> InternalExecutors = new HashSet<string>
    {
        "Start",
        "ResultCollector",
    };
}
