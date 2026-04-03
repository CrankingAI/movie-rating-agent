using MovieRatingAgent.Agent.Executors;

namespace MovieRatingAgent.Tests.Agent;

public class ScorerExecutorsTests
{
    [Theory]
    [InlineData(ScorerExecutors.PopularityId)]
    [InlineData(ScorerExecutors.ArtisticValueId)]
    [InlineData(ScorerExecutors.IconicnessId)]
    public void CreateScorer_ReturnsNonNull_ForValidCategory(string category)
    {
        var scorer = ScorerExecutors.CreateScorer(category);
        Assert.NotNull(scorer);
    }

    [Fact]
    public void CreateScorer_Throws_ForUnknownCategory()
    {
        Assert.Throws<ArgumentException>(() => ScorerExecutors.CreateScorer("UnknownCategory"));
    }
}
