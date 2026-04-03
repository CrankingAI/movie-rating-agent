using MovieRatingAgent.Core.Models;
using Xunit.Abstractions;

namespace MovieRatingAgent.Eval;

public class StabilityVarianceEvalTests
{
    private readonly ITestOutputHelper _output;

    public StabilityVarianceEvalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [RequiresFoundryTheory]
    [MemberData(nameof(MovieEvalSettings.GetScoreExpectations), MemberType = typeof(MovieEvalSettings))]
    public async Task Agent_KeepsScoreSpreadWithinExpectedVariance(MovieScoreExpectation expectation)
    {
        var runCount = MovieEvalSettings.GetRunCount();
        var scores = await CollectScoresAsync(expectation.Title, runCount);
        var minScore = scores.Min();
        var maxScore = scores.Max();
        var spread = maxScore - minScore;

        EvalOutput.WriteLine(_output, $"Expected maximum spread for {expectation.Title}: {expectation.MaxSpread}");
        EvalOutput.WriteLine(_output, $"Observed scores: {string.Join(", ", scores)}");
        EvalOutput.WriteLine(_output, $"Min score: {minScore}; Max score: {maxScore}; Spread: {spread}");

        Assert.True(
            spread <= expectation.MaxSpread,
            $"Expected score spread for '{expectation.Title}' to be <= {expectation.MaxSpread}, but observed {spread}. Scores: [{string.Join(", ", scores)}]");
    }

    private static async Task<IReadOnlyList<int>> CollectScoresAsync(string movieTitle, int runCount)
    {
        var agent = TestHelpers.CreateAgent();
        var scores = new List<int>(runCount);

        for (var index = 0; index < runCount; index++)
        {
            var response = await agent.RunAsync(new JobRequest { Topic = movieTitle });
            scores.Add(response.Score);
        }

        return scores;
    }
}
