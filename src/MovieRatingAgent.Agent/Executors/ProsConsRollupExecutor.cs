using System.Text.Json;
using Microsoft.Extensions.AI;
using MovieRatingAgent.Agent.Models;

namespace MovieRatingAgent.Agent.Executors;

public static class ProsConsRollupExecutor
{
    public const string Id = "ProsConsRollup";

    private static readonly string SystemPrompt =
        """
        You are a synthesis analyst. You will receive multiple sets of PROS and CONS
        from different evaluation categories for a movie. Your job is to:
        1. Merge all PROS into a single deduplicated list of unique positive points.
        2. Merge all CONS into a single deduplicated list of unique negative points.
        3. Identify CONFLICTS — cases where one category's PRO directly contradicts
           another category's CON (e.g., "massive box office success" vs "critics
           panned it as vapid"). List each conflict as a brief statement.
        Semantically merge similar points rather than just removing exact duplicates.
        """;

    public static async Task<ProsConsRollupResult> RunAsync(
        IReadOnlyList<ScorerResult> scorerResults,
        IChatClient chatClient,
        CancellationToken ct)
    {
        var userMessage = FormatScorerResults(scorerResults);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage)
        };

        try
        {
            var response = await chatClient.GetResponseAsync<RollupLlmOutput>(messages, cancellationToken: ct);

            if (response.Result is { } output)
            {
                return new ProsConsRollupResult
                {
                    Pros = output.Pros ?? [],
                    Cons = output.Cons ?? [],
                    Conflicts = output.Conflicts ?? [],
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Structured output failed — fall through to concatenation fallback
        }

        return FallbackConcatenation(scorerResults);
    }

    private static string FormatScorerResults(IReadOnlyList<ScorerResult> results)
    {
        var parts = results.Select(r =>
            $"""
             Category: {r.Category}
             Score: {r.Score}/100
             PROS:
             {string.Join("\n", r.Pros.Select(p => $"- {p}"))}
             CONS:
             {string.Join("\n", r.Cons.Select(c => $"- {c}"))}
             """);

        return "Merge the following evaluation results:\n\n" + string.Join("\n---\n", parts);
    }

    private static ProsConsRollupResult FallbackConcatenation(IReadOnlyList<ScorerResult> results)
    {
        return new ProsConsRollupResult
        {
            Pros = results.SelectMany(r => r.Pros.Select(p => $"[{r.Category}] {p}")).ToList(),
            Cons = results.SelectMany(r => r.Cons.Select(c => $"[{r.Category}] {c}")).ToList(),
            Conflicts = ["Structured rollup failed — conflicts could not be identified"],
        };
    }

    private record RollupLlmOutput(
        IReadOnlyList<string> Pros,
        IReadOnlyList<string> Cons,
        IReadOnlyList<string> Conflicts);
}
