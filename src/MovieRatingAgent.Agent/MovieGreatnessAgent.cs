using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MovieRatingAgent.Agent.Declarative;
using MovieRatingAgent.Agent.Executors;
using MovieRatingAgent.Agent.Models;
using MovieRatingAgent.Core;
using MovieRatingAgent.Core.Models;
using MovieRatingAgent.Core.Observability;

namespace MovieRatingAgent.Agent;

public class MovieGreatnessAgent
{
    private static readonly ActivitySource ActivitySourceInstance = new("MovieRatingAgent.Agent");
    private static readonly string TitleResolutionPrompt =
        """
        You resolve movie titles for a rating system.
        Given a user-provided movie request, return the most likely exact movie title that should be rated.
        Rules:
        - Keep RequestedMovie exactly as provided.
        - Set RatedMovie to the canonical movie title you believe the user intended.
        - Set ReleaseYear to the original theatrical release year of the movie.
        - Set InfoUrl to the best URL for more information about this movie, in this priority order:
          1. The movie's own official website (if one exists and is still live)
          2. The movie's Wikipedia page (e.g. https://en.wikipedia.org/wiki/Heat_(1995_film))
          3. The movie's IMDb page (e.g. https://www.imdb.com/title/tt0113277/)
          4. If none of the above are known, use a Google search URL: https://www.google.com/search?q=MovieName+year+film
        - Fix obvious punctuation, spacing, apostrophes, accents, subtitles, and common misspellings when confidence is high.
        - If the request is already a clear exact movie title, keep RatedMovie the same as RequestedMovie.
        - Do not add commentary.
        """;

    private readonly IChatClient _chatClient;
    private readonly ILogger<MovieGreatnessAgent> _logger;
    private readonly string _modelId;

    public MovieGreatnessAgent(IChatClient chatClient, ILogger<MovieGreatnessAgent> logger, AgentOptions options)
    {
        _chatClient = chatClient;
        _logger = logger;
        _modelId = options.ModelId;
    }

    public async Task<JobResponse> RunAsync(JobRequest request, CancellationToken ct = default)
    {
        using var activity = ActivitySourceInstance.StartActivity(
            "invoke_agent MovieGreatnessAgent",
            ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.agent.name", "MovieGreatnessAgent");
        activity?.SetTag("gen_ai.agent.version", AgentVersion.Current);
        activity?.SetTag(TelemetryTags.MovieRequested, request.Topic);

        var movieSelection = await ResolveMovieSelectionAsync(request.Topic, ct);
        activity?.SetTag(TelemetryTags.MovieRequested, movieSelection.RequestedMovie);
        activity?.SetTag(TelemetryTags.MovieRated, movieSelection.RatedMovie);

        var workflow = BuildWorkflow();
        var run = await InProcessExecution.RunAsync<string>(workflow, movieSelection.RatedMovie, sessionId: Guid.NewGuid().ToString(), ct);

        WorkflowResult? workflowResult = null;
        foreach (var evt in run.OutgoingEvents)
        {
            if (evt is WorkflowOutputEvent outputEvt && outputEvt.Is<WorkflowResult>(out var result))
            {
                workflowResult = result;
                break;
            }
        }

        if (workflowResult is null)
        {
            return new JobResponse
            {
                RequestedMovie = movieSelection.RequestedMovie,
                RatedMovie = movieSelection.RatedMovie,
                ReleaseYear = movieSelection.ReleaseYear,
                InfoUrl = movieSelection.InfoUrl,
                Score = -1,
                Reasoning = "Workflow did not produce a result.",
            };
        }

        var reasoning = $"Popularity: {workflowResult.SubScores.Popularity}/100 (30%), " +
                        $"Artistic Value: {workflowResult.SubScores.ArtisticValue}/100 (40%), " +
                        $"Iconicness: {workflowResult.SubScores.Iconicness}/100 (30%) → " +
                        $"Weighted Score: {workflowResult.Score}/100";

        return new JobResponse
        {
            RequestedMovie = movieSelection.RequestedMovie,
            RatedMovie = movieSelection.RatedMovie,
            ReleaseYear = movieSelection.ReleaseYear,
            InfoUrl = movieSelection.InfoUrl,
            Score = workflowResult.Score,
            Reasoning = reasoning,
            SubScores = new SubScores
            {
                Popularity = workflowResult.SubScores.Popularity,
                ArtisticValue = workflowResult.SubScores.ArtisticValue,
                Iconicness = workflowResult.SubScores.Iconicness,
            },
            Pros = workflowResult.ProsConsRollup.Pros,
            Cons = workflowResult.ProsConsRollup.Cons,
            Conflicts = workflowResult.ProsConsRollup.Conflicts,
        };
    }

    private async Task<MovieSelection> ResolveMovieSelectionAsync(string requestedMovie, CancellationToken ct)
    {
        using var activity = ActivitySourceInstance.StartActivity("chat ResolveMovieTitle");
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.agent.name", "ResolveMovieTitle");
        activity?.SetTag("gen_ai.request.model", _modelId);
        activity?.SetTag("gen_ai.provider.name", "azure.ai.inference");
        activity?.SetTag("azure.resource_provider.namespace", "Microsoft.CognitiveServices");

        var normalizedRequestedMovie = requestedMovie.Trim();

        try
        {
            var response = await _chatClient.GetResponseAsync<MovieSelection>(
                [
                    new(ChatRole.System, TitleResolutionPrompt),
                    new(ChatRole.User, normalizedRequestedMovie)
                ],
                cancellationToken: ct);

            var selection = response.Result;
            if (selection is not null
                && !string.IsNullOrWhiteSpace(selection.RequestedMovie)
                && !string.IsNullOrWhiteSpace(selection.RatedMovie))
            {
                var resolvedRequestedMovie = selection.RequestedMovie?.Trim();
                var resolvedRatedMovie = selection.RatedMovie?.Trim();

                if (!string.IsNullOrWhiteSpace(resolvedRequestedMovie)
                    && !string.IsNullOrWhiteSpace(resolvedRatedMovie))
                {
                    return new MovieSelection(resolvedRequestedMovie, resolvedRatedMovie, selection.ReleaseYear, selection.InfoUrl);
                }

                return selection with
                {
                    RequestedMovie = normalizedRequestedMovie,
                    RatedMovie = normalizedRequestedMovie
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve movie title for request '{RequestedMovie}'", normalizedRequestedMovie);
        }

        return new MovieSelection(normalizedRequestedMovie, normalizedRequestedMovie, null, null);
    }

    /// <summary>
    /// Builds the workflow DAG and returns the Mermaid diagram string.
    /// Usable without a real IChatClient — the executors are never invoked.
    /// </summary>
    public static string GetWorkflowMermaid(IChatClient chatClient, string modelId = "gpt-5.4")
    {
        var workflow = BuildWorkflowStatic(chatClient, modelId);
        return WorkflowVisualizer.ToMermaidString(workflow);
    }

    private Workflow BuildWorkflow()
    {
        var workflow = BuildWorkflowStatic(_chatClient, _modelId);
        EmitMermaidDiagram(workflow);
        return workflow;
    }

    internal static Workflow BuildWorkflowStatic(IChatClient chatClient, string modelId)
    {
        Func<string, string> forwardRequestedMovie = static movie => movie;

        var start = ExecutorBindingExtensions.BindAsExecutor(
            forwardRequestedMovie!, "Start");

        var agentsDir = ResolveAgentsDirectory();
        var popularityScorer = CreateAgentBinding(
            DeclarativeScorer.FromYamlFile(Path.Combine(agentsDir, "popularity-scorer.yaml"), chatClient), modelId);
        var artisticScorer = CreateAgentBinding(
            DeclarativeScorer.FromYamlFile(Path.Combine(agentsDir, "artistic-value-scorer.yaml"), chatClient), modelId);
        var iconicnessScorer = CreateAgentBinding(
            DeclarativeScorer.FromYamlFile(Path.Combine(agentsDir, "iconicness-scorer.yaml"), chatClient), modelId);

        var collectedResults = new List<ScorerResult>();
        var resultCollector = ExecutorBindingExtensions.BindAsExecutor<ScorerResult, ScorerResultSet>(
            (ScorerResult result) =>
            {
                collectedResults.Add(result);
                return new ScorerResultSet
                {
                    Results = collectedResults.ToList()
                };
            },
            "ResultCollector");

        var prosConsRollup = ExecutorBindingExtensions.BindAsExecutor<ScorerResultSet, RollupInput>(
            async (ScorerResultSet scorerResultSet, IWorkflowContext ctx, CancellationToken ct) =>
            {
                using var activity = ActivitySourceInstance.StartActivity(
                    $"chat {ProsConsRollupExecutor.Id}");
                activity?.SetTag("gen_ai.operation.name", "chat");
                activity?.SetTag("gen_ai.agent.name", ProsConsRollupExecutor.Id);
                activity?.SetTag("gen_ai.request.model", modelId);
                activity?.SetTag("gen_ai.provider.name", "azure.ai.inference");
                activity?.SetTag("azure.resource_provider.namespace", "Microsoft.CognitiveServices");

                var rollup = await ProsConsRollupExecutor.RunAsync(
                    scorerResultSet.Results, chatClient, ct);

                return new RollupInput
                {
                    ScorerResults = scorerResultSet,
                    ProsConsRollup = rollup,
                };
            },
            ProsConsRollupExecutor.Id);

        var weightedRollup = ExecutorBindingExtensions.BindAsExecutor<RollupInput, WorkflowResult>(
            (RollupInput input) =>
            {
                var (score, subScores) = WeightedScoreRollupExecutor.Calculate(
                    input.ScorerResults.Results);

                return new WorkflowResult
                {
                    Score = score,
                    ProsConsRollup = input.ProsConsRollup,
                    SubScores = subScores,
                };
            },
            WeightedScoreRollupExecutor.Id);

        var scorers = new[] { popularityScorer, artisticScorer, iconicnessScorer };

        return new WorkflowBuilder(start)
            .WithName("MovieGreatnessWorkflow")
            .AddFanOutEdge(start, scorers)
            .AddFanInBarrierEdge(scorers, resultCollector)
            .AddEdge<ScorerResultSet>(resultCollector, prosConsRollup, resultSet => resultSet?.Results?.Count == 3)
            .AddEdge(prosConsRollup, weightedRollup)
            .WithOutputFrom(weightedRollup)
            .WithOpenTelemetry(opts =>
            {
                opts.EnableSensitiveData = false;
            }, ActivitySourceInstance)
            .Build();
    }

    private static ExecutorBinding CreateAgentBinding(DeclarativeScorer scorer, string modelId)
    {
        return ExecutorBindingExtensions.BindAsExecutor<string, ScorerResult>(
            async (string movieName, IWorkflowContext ctx, CancellationToken ct) =>
            {
                using var activity = ActivitySourceInstance.StartActivity($"chat {scorer.Id}");
                activity?.SetTag("gen_ai.operation.name", "chat");
                activity?.SetTag("gen_ai.agent.name", scorer.Id);
                activity?.SetTag("gen_ai.request.model", modelId);
                activity?.SetTag("gen_ai.provider.name", "azure.ai.inference");
                activity?.SetTag("azure.resource_provider.namespace", "Microsoft.CognitiveServices");

                return await scorer.RunAsync(movieName, ct);
            },
            scorer.Id);
    }

    /// <summary>
    /// Locates the agents/ directory. Conventionally it's <c>{AppContext.BaseDirectory}/agents</c>
    /// (Content-copied from the repo root by each consuming project), but for callers that
    /// run from non-standard working directories we walk up looking for a sibling <c>agents/</c>
    /// folder containing the expected scorer YAMLs.
    /// </summary>
    private static string ResolveAgentsDirectory()
    {
        var primary = Path.Combine(AppContext.BaseDirectory, "agents");
        if (File.Exists(Path.Combine(primary, "popularity-scorer.yaml")))
            return primary;

        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "agents");
            if (File.Exists(Path.Combine(candidate, "popularity-scorer.yaml")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            $"Could not locate scorer YAML files. Expected at '{primary}' or a parent directory's 'agents/' folder.");
    }

    private void EmitMermaidDiagram(Workflow workflow)
    {
        try
        {
            var repoRoot = FindRepoRoot();
            if (repoRoot is not null)
            {
                var path = Path.Combine(repoRoot, "agent-workflow.mmd");
                var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
                File.WriteAllText(path, mermaid);
                _logger.LogInformation("Workflow diagram written to {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write workflow diagram");
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "MovieRatingAgent.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private sealed record MovieSelection(string RequestedMovie, string RatedMovie, int? ReleaseYear, string? InfoUrl);
}
