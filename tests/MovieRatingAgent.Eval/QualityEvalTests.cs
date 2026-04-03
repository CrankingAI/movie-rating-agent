using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace MovieRatingAgent.Eval;

public class QualityEvalTests
{
    [RequiresFoundryFact]
    public async Task Agent_Output_MeetsQualityMetrics()
    {
        var chatClient = TestHelpers.CreateChatClient();

        var coherenceEvaluator = new CoherenceEvaluator();
        var relevanceEvaluator = new RelevanceEvaluator();
        var compositeEvaluator = new CompositeEvaluator(coherenceEvaluator, relevanceEvaluator);

        var chatConfig = new ChatConfiguration(chatClient);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Rate the movie: The Godfather")
        };

        var response = await chatClient.GetResponseAsync(messages);

        var result = await compositeEvaluator.EvaluateAsync(messages, response, chatConfig);

        foreach (var metric in result.Metrics)
        {
            if (metric.Value is NumericMetric numericMetric)
            {
                Assert.True(numericMetric.Value >= 3,
                    $"Metric '{metric.Key}' scored {numericMetric.Value}, expected >= 3");
            }

            var errors = metric.Value.Diagnostics?
                .Where(d => d.Severity == EvaluationDiagnosticSeverity.Error)
                .ToList();
            Assert.True(errors is null or [],
                $"Metric '{metric.Key}' had evaluation errors");
        }
    }

    [RequiresFoundryFact]
    public async Task Agent_Rating_IsNumericAndInRange()
    {
        var agent = TestHelpers.CreateAgent();
        var request = new MovieRatingAgent.Core.Models.JobRequest { Topic = "Citizen Kane" };

        var response = await agent.RunAsync(request);

        Assert.InRange(response.Score, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(response.Reasoning));
    }
}
