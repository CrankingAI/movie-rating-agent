using MovieRatingAgent.Agent.Models;

namespace MovieRatingAgent.Agent.Executors;

public static class WeightedScoreRollupExecutor
{
    public const string Id = "WeightedScoreRollup";

    private const double PopularityWeight = 0.30;
    private const double ArtisticValueWeight = 0.40;
    private const double IconicnessWeight = 0.30;

    public static (int Score, SubScoreSet SubScores) Calculate(IReadOnlyList<ScorerResult> scorerResults)
    {
        var popularity = scorerResults.FirstOrDefault(r => r.Category == ScorerExecutors.PopularityId);
        var artistic = scorerResults.FirstOrDefault(r => r.Category == ScorerExecutors.ArtisticValueId);
        var iconicness = scorerResults.FirstOrDefault(r => r.Category == ScorerExecutors.IconicnessId);

        var popScore = popularity?.Score ?? 50;
        var artScore = artistic?.Score ?? 50;
        var icoScore = iconicness?.Score ?? 50;

        var weighted = (popScore * PopularityWeight)
                     + (artScore * ArtisticValueWeight)
                     + (icoScore * IconicnessWeight);

        var subScores = new SubScoreSet
        {
            Popularity = popScore,
            ArtisticValue = artScore,
            Iconicness = icoScore,
        };

        return (Math.Clamp((int)Math.Round(weighted), 0, 100), subScores);
    }
}
