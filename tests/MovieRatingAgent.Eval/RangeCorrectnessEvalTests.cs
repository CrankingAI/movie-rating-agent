using MovieRatingAgent.Core.Models;
using Xunit.Abstractions;

namespace MovieRatingAgent.Eval;

public class RangeCorrectnessEvalTests
{
    private readonly ITestOutputHelper _output;

    public RangeCorrectnessEvalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [RequiresFoundryTheory]
    [MemberData(nameof(MovieEvalSettings.GetScoreExpectations), MemberType = typeof(MovieEvalSettings))]
    public async Task Agent_Usually_ScoresWithinExpectedRange(MovieScoreExpectation expectation)
    {
        var runCount = MovieEvalSettings.GetRunCount();
        var requiredInRangeCount = MovieEvalSettings.GetRequiredInRangeCount(runCount);
        var scores = await CollectScoresAsync(expectation.Title, runCount);

        var inRangeCount = scores.Count(score => score >= expectation.MinScore && score <= expectation.MaxScore);

        EvalOutput.WriteLine(_output, $"Expected range for {expectation.Title}: {expectation.MinScore}-{expectation.MaxScore}");
        EvalOutput.WriteLine(_output, $"Observed scores: {string.Join(", ", scores)}");
        EvalOutput.WriteLine(_output, $"In-range runs: {inRangeCount}/{runCount}; required: {requiredInRangeCount}/{runCount}");

        Assert.True(
            inRangeCount >= requiredInRangeCount,
            $"Expected at least {requiredInRangeCount}/{runCount} scores for '{expectation.Title}' to land in range [{expectation.MinScore}, {expectation.MaxScore}], but observed {inRangeCount}/{runCount}. Scores: [{string.Join(", ", scores)}]");
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
