using System.Text.Json;
using Microsoft.Extensions.AI;
using MovieRatingAgent.Agent.Models;

namespace MovieRatingAgent.Agent.Executors;

public static class ScorerExecutors
{
    public const string PopularityId = "PopularityScorer";
    public const string ArtisticValueId = "ArtisticValueScorer";
    public const string IconicnessId = "IconicnessScorer";

    private static readonly string PopularitySystemPrompt =
        """
        You are a movie popularity analyst. Given a movie name, evaluate its POPULARITY
        on a scale from 0 (completely unknown) to 100 (universally known blockbuster).
        Consider box office performance, audience reach, streaming popularity, and
        mainstream recognition. Provide specific PROS (factors boosting popularity)
        and CONS (factors limiting popularity).
        """;

    private static readonly string ArtisticValueSystemPrompt =
        """
        You are an expert film critic focused on artistic merit. Given a movie name,
        evaluate its ARTISTIC VALUE on a scale from 0 (no artistic merit) to 100
        (a perfect artistic achievement). Consider cinematography, direction, acting,
        screenplay, editing, and whether it won or was nominated for major awards like
        the Academy Award for Best Picture. Provide specific PROS (artistic strengths)
        and CONS (artistic weaknesses).
        """;

    private static readonly string IconicnessSystemPrompt =
        """
        You are a cultural historian specializing in cinema. Given a movie name,
        evaluate its ICONICNESS on a scale from 0 (no cultural footprint) to 100
        (deeply embedded in global culture). Consider memorable quotes
        (e.g. "I'm gonna make him an offer he can't refuse", "May the Force be with you",
        "these go to eleven"), iconic scenes, influence on other films, parodies and
        references in popular culture, and lasting cultural impact. Provide specific
        PROS (iconic elements) and CONS (factors limiting cultural impact).
        """;

    public static string GetSystemPrompt(string category) => category switch
    {
        PopularityId => PopularitySystemPrompt,
        ArtisticValueId => ArtisticValueSystemPrompt,
        IconicnessId => IconicnessSystemPrompt,
        _ => throw new ArgumentException($"Unknown scorer category: {category}")
    };

    public static Func<string, IChatClient, CancellationToken, Task<ScorerResult>> CreateScorer(string category)
    {
        var systemPrompt = GetSystemPrompt(category);

        return async (movieName, chatClient, ct) =>
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, $"Evaluate the movie: {movieName}")
            };

            try
            {
                var response = await chatClient.GetResponseAsync<ScorerLlmOutput>(messages, cancellationToken: ct);

                if (response.Result is { } output)
                {
                    return new ScorerResult
                    {
                        Category = category,
                        Score = Math.Clamp(output.Score, 0, 100),
                        Pros = output.Pros ?? [],
                        Cons = output.Cons ?? [],
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Structured output failed — return a fallback
            }

            return new ScorerResult
            {
                Category = category,
                Score = 50,
                Pros = [$"Could not evaluate {category} — defaulting to 50"],
                Cons = ["Structured output parsing failed"],
            };
        };
    }

    private record ScorerLlmOutput(int Score, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons);
}
