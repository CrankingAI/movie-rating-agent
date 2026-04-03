using MovieRatingAgent.Agent.Executors;
using MovieRatingAgent.Agent.Models;

namespace MovieRatingAgent.Tests.Agent;

public class WeightedScoreRollupExecutorTests
{
    [Fact]
    public void Calculate_AppliesWeights_Correctly()
    {
        var results = new List<ScorerResult>
        {
            new() { Category = ScorerExecutors.PopularityId, Score = 80, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.ArtisticValueId, Score = 90, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.IconicnessId, Score = 70, Pros = [], Cons = [] },
        };

        var (score, subScores) = WeightedScoreRollupExecutor.Calculate(results);

        Assert.Equal(81, score);
        Assert.Equal(80, subScores.Popularity);
        Assert.Equal(90, subScores.ArtisticValue);
        Assert.Equal(70, subScores.Iconicness);
    }

    [Fact]
    public void Calculate_AllZeros_ReturnsZero()
    {
        var results = new List<ScorerResult>
        {
            new() { Category = ScorerExecutors.PopularityId, Score = 0, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.ArtisticValueId, Score = 0, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.IconicnessId, Score = 0, Pros = [], Cons = [] },
        };

        var (score, _) = WeightedScoreRollupExecutor.Calculate(results);
        Assert.Equal(0, score);
    }

    [Fact]
    public void Calculate_AllHundred_ReturnsHundred()
    {
        var results = new List<ScorerResult>
        {
            new() { Category = ScorerExecutors.PopularityId, Score = 100, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.ArtisticValueId, Score = 100, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.IconicnessId, Score = 100, Pros = [], Cons = [] },
        };

        var (score, _) = WeightedScoreRollupExecutor.Calculate(results);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Calculate_MissingCategories_DefaultsToFifty()
    {
        var results = new List<ScorerResult>
        {
            new() { Category = ScorerExecutors.PopularityId, Score = 100, Pros = [], Cons = [] },
        };

        var (score, subScores) = WeightedScoreRollupExecutor.Calculate(results);

        Assert.Equal(65, score);
        Assert.Equal(100, subScores.Popularity);
        Assert.Equal(50, subScores.ArtisticValue);
        Assert.Equal(50, subScores.Iconicness);
    }

    [Fact]
    public void Calculate_Rounds_Correctly()
    {
        var results = new List<ScorerResult>
        {
            new() { Category = ScorerExecutors.PopularityId, Score = 33, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.ArtisticValueId, Score = 33, Pros = [], Cons = [] },
            new() { Category = ScorerExecutors.IconicnessId, Score = 33, Pros = [], Cons = [] },
        };

        var (score, _) = WeightedScoreRollupExecutor.Calculate(results);
        Assert.Equal(33, score);
    }
}
