using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MovieRatingAgent.Agent.Models;

namespace MovieRatingAgent.Agent.Declarative;

/// <summary>
/// Runs a scorer as a Microsoft Agent Framework <see cref="ChatClientAgent"/>
/// configured from an externally-loaded YAML file. The agent emits a structured
/// JSON response (Score / Pros / Cons) which this class deserializes into a
/// <see cref="ScorerResult"/> for the rest of the workflow.
/// </summary>
public sealed class DeclarativeScorer
{
    private readonly ScorerAgentDefinition _definition;
    private readonly ChatClientAgent _agent;

    public string Id => _definition.Name;

    public DeclarativeScorer(ScorerAgentDefinition definition, IChatClient chatClient)
    {
        _definition = definition;

        var chatOptions = new ChatOptions
        {
            Instructions = definition.Instructions,
        };
        if (definition.Model?.Options?.Temperature is { } temperature)
        {
            chatOptions.Temperature = temperature;
        }
        if (definition.Model?.Options?.TopP is { } topP)
        {
            chatOptions.TopP = topP;
        }

        _agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions,
        });
    }

    public static DeclarativeScorer FromYamlFile(string path, IChatClient chatClient)
        => new(ScorerAgentDefinition.LoadFromFile(path), chatClient);

    public async Task<ScorerResult> RunAsync(string movieName, CancellationToken ct = default)
    {
        try
        {
            var response = await _agent.RunAsync<ScorerLlmOutput>(
                $"Evaluate the movie: {movieName}",
                cancellationToken: ct);

            if (response.Result is { } output)
            {
                return new ScorerResult
                {
                    Category = _definition.Name,
                    Score = Math.Clamp(output.Score, 0, 100),
                    Pros = output.Pros ?? [],
                    Cons = output.Cons ?? [],
                };
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }

        return new ScorerResult
        {
            Category = _definition.Name,
            Score = 50,
            Pros = [$"Could not evaluate {_definition.Name} — defaulting to 50"],
            Cons = ["Structured output parsing failed"],
        };
    }

    private sealed record ScorerLlmOutput(int Score, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons);
}
