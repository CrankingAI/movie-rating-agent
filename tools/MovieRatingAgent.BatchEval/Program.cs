using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using MovieRatingAgent.Agent.Executors;
using MovieRatingAgent.Agent.Models;
using MovieRatingAgent.BatchEval;

// Support --analyze-only mode to re-run analysis on existing results
if (args.Length > 0 && args[0] == "--analyze-only")
{
    var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "eval-results");
    await AnalyzeResults.RunAnalysis(dir);
    return 0;
}

var JsonOpts = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
};

// ── Configuration ────────────────────────────────────────────────────────────
var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_ENDPOINT env var required");
var apiKey = Environment.GetEnvironmentVariable("FOUNDRY_API_KEY")
    ?? throw new InvalidOperationException("FOUNDRY_API_KEY env var required");

var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "eval-results");
Directory.CreateDirectory(outputDir);

var totalSw = Stopwatch.StartNew();

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          MovieRatingAgent Batch Evaluation Runner            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Endpoint:       {endpoint}");
Console.WriteLine($"  Models:         {string.Join(", ", EvalMatrix.Models)}");
Console.WriteLine($"  Prompts:        {string.Join(", ", EvalMatrix.PromptVariants)}");
Console.WriteLine($"  Temperatures:   {string.Join(", ", EvalMatrix.Temperatures)}");
Console.WriteLine($"  Movies:         {string.Join(", ", EvalMatrix.Movies.Select(m => m.Title))}");
Console.WriteLine($"  Runs/combo:     {EvalMatrix.RunsPerCombination}");
Console.WriteLine($"  Total combos:   {EvalMatrix.TotalCombinations}");
Console.WriteLine($"  Total runs:     {EvalMatrix.TotalRuns}");
Console.WriteLine($"  Output:         {outputDir}");
Console.WriteLine();
Console.Out.Flush();

// ── Run all combinations ─────────────────────────────────────────────────────
// Direct scorer calls (bypass workflow framework for speed & reliability).
// Each "run" calls 3 scorers in parallel, then computes weighted rollup.
var allResults = new ConcurrentBag<SingleRunResult>();
var completedRuns = 0;
var failedRuns = 0;

foreach (var model in EvalMatrix.Models)
{
    foreach (var promptVariant in EvalMatrix.PromptVariants)
    {
        foreach (var temp in EvalMatrix.Temperatures)
        {
            var comboKey = $"{model}|{promptVariant}|temp={temp}";
            Console.WriteLine($"── {comboKey} ──");
            Console.Out.Flush();

            var chatClient = CreateChatClient(endpoint, apiKey, model);

            foreach (var (movieTitle, _, _) in EvalMatrix.Movies)
            {
                for (var run = 0; run < EvalMatrix.RunsPerCombination; run++)
                {
                    var runSw = Stopwatch.StartNew();
                    try
                    {
                        // Run 3 scorers in parallel
                        var popTask = RunScorerAsync(chatClient, ScorerExecutors.PopularityId, movieTitle, promptVariant, temp);
                        var artTask = RunScorerAsync(chatClient, ScorerExecutors.ArtisticValueId, movieTitle, promptVariant, temp);
                        var icoTask = RunScorerAsync(chatClient, ScorerExecutors.IconicnessId, movieTitle, promptVariant, temp);

                        await Task.WhenAll(popTask, artTask, icoTask);

                        var results = new List<ScorerResult> { popTask.Result, artTask.Result, icoTask.Result };
                        var (score, subScores) = WeightedScoreRollupExecutor.Calculate(results);

                        runSw.Stop();

                        allResults.Add(new SingleRunResult
                        {
                            Model = model,
                            PromptVariant = promptVariant,
                            Temperature = temp,
                            Movie = movieTitle,
                            RunIndex = run,
                            Score = score,
                            PopularityScore = subScores.Popularity,
                            ArtisticValueScore = subScores.ArtisticValue,
                            IconicnessScore = subScores.Iconicness,
                            LatencySeconds = runSw.Elapsed.TotalSeconds,
                            Error = null,
                        });

                        Interlocked.Increment(ref completedRuns);
                    }
                    catch (Exception ex)
                    {
                        runSw.Stop();
                        allResults.Add(new SingleRunResult
                        {
                            Model = model,
                            PromptVariant = promptVariant,
                            Temperature = temp,
                            Movie = movieTitle,
                            RunIndex = run,
                            Score = -1,
                            LatencySeconds = runSw.Elapsed.TotalSeconds,
                            Error = ex.Message,
                        });
                        Interlocked.Increment(ref failedRuns);
                        Console.WriteLine($"    [FAIL] run {run}: {ex.Message}");
                        Console.Out.Flush();
                    }

                    var total = completedRuns + failedRuns;
                    if (run % 10 == 0 || run == EvalMatrix.RunsPerCombination - 1)
                    {
                        Console.WriteLine($"    {movieTitle}: {run + 1}/{EvalMatrix.RunsPerCombination} " +
                                          $"(total: {total}/{EvalMatrix.TotalRuns}, " +
                                          $"elapsed: {totalSw.Elapsed:hh\\:mm\\:ss})");
                        Console.Out.Flush();
                    }
                }

                // Write incremental results after each movie completes
                var comboFileName = $"{model}_{promptVariant}_t{temp}_{SanitizeFileName(movieTitle)}.json";
                var comboResults = allResults
                    .Where(r => r.Model == model && r.PromptVariant == promptVariant
                        && r.Temperature == temp && r.Movie == movieTitle)
                    .OrderBy(r => r.RunIndex)
                    .ToList();
                await File.WriteAllTextAsync(
                    Path.Combine(outputDir, comboFileName),
                    JsonSerializer.Serialize(comboResults, JsonOpts));
                Console.WriteLine($"    >> Wrote {comboFileName}");
                Console.Out.Flush();
            }
        }
    }
}

// ── Quality evaluation (M.E.AI.Evaluation: Coherence + Relevance + Groundedness) ──
Console.WriteLine();
Console.WriteLine("── M.E.AI.Evaluation quality checks ──");
Console.Out.Flush();

var qualityResults = new List<QualityEvalResult>();
foreach (var model in EvalMatrix.Models)
{
    var chatClient = CreateChatClient(endpoint, apiKey, model);
    var coherenceEval = new CoherenceEvaluator();
    var relevanceEval = new RelevanceEvaluator();
    var groundednessEval = new GroundednessEvaluator();
    var composite = new CompositeEvaluator(coherenceEval, relevanceEval, groundednessEval);
    var chatConfig = new ChatConfiguration(chatClient);

    foreach (var (movieTitle, _, _) in EvalMatrix.Movies)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, $"Rate the movie '{movieTitle}' for greatness on a 0-100 scale. " +
                "Evaluate popularity, artistic value, and cultural iconicness. Provide pros and cons.")
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages);
            var evalResult = await composite.EvaluateAsync(messages, response, chatConfig);

            var qr = new QualityEvalResult { Model = model, Movie = movieTitle };
            foreach (var metric in evalResult.Metrics)
            {
                if (metric.Value is NumericMetric nm)
                {
                    qr.Metrics[metric.Key] = nm.Value ?? 0;
                }
            }
            qualityResults.Add(qr);
            Console.WriteLine($"    [{model}] {movieTitle}: " +
                string.Join(", ", qr.Metrics.Select(m => $"{m.Key}={m.Value:F1}")));
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [{model}] {movieTitle}: FAILED - {ex.Message}");
            Console.Out.Flush();
            qualityResults.Add(new QualityEvalResult
            {
                Model = model,
                Movie = movieTitle,
                Error = ex.Message
            });
        }
    }
}

await File.WriteAllTextAsync(
    Path.Combine(outputDir, "quality_eval.json"),
    JsonSerializer.Serialize(qualityResults, JsonOpts));

// ── Statistical Analysis ─────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("── Computing statistical analysis ──");
Console.Out.Flush();

var analysisResults = new Dictionary<string, object>();

var comboStats = new List<object>();
foreach (var combo in allResults
    .GroupBy(r => $"{r.Model}|{r.PromptVariant}|t={r.Temperature}|{r.Movie}"))
{
    var scores = combo.Where(r => r.Score >= 0).Select(r => (double)r.Score).ToList();
    var stats = StatisticalAnalysis.ComputeDescriptive(scores);
    var latencies = combo.Select(r => r.LatencySeconds).ToList();
    var latencyStats = StatisticalAnalysis.ComputeDescriptive(latencies);

    var parts = combo.Key.Split('|');
    comboStats.Add(new
    {
        Model = parts[0],
        PromptVariant = parts[1],
        Temperature = parts[2],
        Movie = parts[3],
        ScoreStats = stats,
        LatencyStats = latencyStats,
        FailedRuns = combo.Count(r => r.Score < 0),
        SuccessfulRuns = combo.Count(r => r.Score >= 0),
    });
}
analysisResults["combination_stats"] = comboStats;

// Pairwise model comparisons
var modelComparisons = new List<object>();
foreach (var group in allResults
    .Where(r => r.Score >= 0)
    .GroupBy(r => $"{r.PromptVariant}|t={r.Temperature}|{r.Movie}"))
{
    var byModel = group.GroupBy(r => r.Model).ToDictionary(g => g.Key, g => g.Select(r => (double)r.Score).ToList());
    var models = byModel.Keys.OrderBy(k => k).ToList();

    for (var i = 0; i < models.Count; i++)
    for (var j = i + 1; j < models.Count; j++)
    {
        var comparison = StatisticalAnalysis.Compare(
            models[i], byModel[models[i]],
            models[j], byModel[models[j]]);

        modelComparisons.Add(new
        {
            Context = group.Key,
            comparison.GroupA,
            comparison.GroupB,
            comparison.MeanDifference,
            comparison.CohensD,
            comparison.EffectSizeLabel,
            comparison.MannWhitneyU,
            comparison.MannWhitneyZ,
            comparison.MannWhitneyP,
            comparison.IsSignificant,
        });
    }
}
analysisResults["model_comparisons"] = modelComparisons;

// Prompt variant comparisons
var promptComparisons = new List<object>();
foreach (var group in allResults
    .Where(r => r.Score >= 0)
    .GroupBy(r => $"{r.Model}|t={r.Temperature}|{r.Movie}"))
{
    var byPrompt = group.GroupBy(r => r.PromptVariant).ToDictionary(g => g.Key, g => g.Select(r => (double)r.Score).ToList());
    if (byPrompt.Count >= 2)
    {
        var prompts = byPrompt.Keys.OrderBy(k => k).ToList();
        var comparison = StatisticalAnalysis.Compare(
            prompts[0], byPrompt[prompts[0]],
            prompts[1], byPrompt[prompts[1]]);

        promptComparisons.Add(new
        {
            Context = group.Key,
            comparison.GroupA,
            comparison.GroupB,
            comparison.MeanDifference,
            comparison.CohensD,
            comparison.EffectSizeLabel,
            comparison.MannWhitneyP,
            comparison.IsSignificant,
        });
    }
}
analysisResults["prompt_comparisons"] = promptComparisons;

// Temperature comparisons
var tempComparisons = new List<object>();
foreach (var group in allResults
    .Where(r => r.Score >= 0)
    .GroupBy(r => $"{r.Model}|{r.PromptVariant}|{r.Movie}"))
{
    var byTemp = group.GroupBy(r => r.Temperature).ToDictionary(g => $"t={g.Key}", g => g.Select(r => (double)r.Score).ToList());
    if (byTemp.Count >= 2)
    {
        var temps = byTemp.Keys.OrderBy(k => k).ToList();
        var comparison = StatisticalAnalysis.Compare(
            temps[0], byTemp[temps[0]],
            temps[1], byTemp[temps[1]]);

        tempComparisons.Add(new
        {
            Context = group.Key,
            comparison.GroupA,
            comparison.GroupB,
            comparison.MeanDifference,
            comparison.CohensD,
            comparison.EffectSizeLabel,
            comparison.MannWhitneyP,
            comparison.IsSignificant,
        });
    }
}
analysisResults["temperature_comparisons"] = tempComparisons;
analysisResults["quality_eval"] = qualityResults;

await File.WriteAllTextAsync(
    Path.Combine(outputDir, "analysis.json"),
    JsonSerializer.Serialize(analysisResults, JsonOpts));

// ── Summary ──────────────────────────────────────────────────────────────────
totalSw.Stop();
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                    EVALUATION COMPLETE                       ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine($"  Total time:     {totalSw.Elapsed:hh\\:mm\\:ss}");
Console.WriteLine($"  Successful:     {completedRuns}");
Console.WriteLine($"  Failed:         {failedRuns}");
Console.WriteLine($"  Results:        {outputDir}");
Console.WriteLine();

Console.WriteLine("  Model               Prompt    Temp  Movie                           Mean   StdDev  CI95           Range");
Console.WriteLine("  ─────               ──────    ────  ─────                           ────   ──────  ────           ─────");
foreach (var stat in comboStats.Cast<dynamic>().OrderBy(s => $"{s.Model}{s.PromptVariant}{s.Temperature}{s.Movie}"))
{
    var ss = (StatisticalAnalysis.DescriptiveStats)stat.ScoreStats;
    Console.WriteLine($"  {stat.Model,-20} {stat.PromptVariant,-9} {stat.Temperature,-5} {stat.Movie,-30} {ss.Mean,6:F1} {ss.StdDev,7:F2}  [{ss.CI95Lower:F1}, {ss.CI95Upper:F1}]  [{ss.Min}-{ss.Max}]");
}

Console.WriteLine();
Console.WriteLine("  Significant model comparisons (p < 0.05):");
foreach (var mc in modelComparisons.Cast<dynamic>().Where(c => (bool)c.IsSignificant))
{
    Console.WriteLine($"    {mc.Context}: {mc.GroupA} vs {mc.GroupB} — d={mc.CohensD:F2} ({mc.EffectSizeLabel}), p={mc.MannWhitneyP:F4}");
}

Console.WriteLine();
Console.WriteLine("  Quality eval summary:");
foreach (var qr in qualityResults.Where(q => q.Error is null))
{
    Console.WriteLine($"    [{qr.Model}] {qr.Movie}: {string.Join(", ", qr.Metrics.Select(m => $"{m.Key}={m.Value:F1}"))}");
}

return 0;

// ── Helper functions ─────────────────────────────────────────────────────────

static IChatClient CreateChatClient(string endpoint, string apiKey, string modelId)
{
    var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
    var chatClient = azureClient.GetChatClient(modelId).AsIChatClient();
    return new ChatClientBuilder(chatClient)
        .UseFunctionInvocation()
        .Build();
}

static async Task<ScorerResult> RunScorerAsync(
    IChatClient chatClient, string category, string movieTitle,
    string promptVariant, float temperature)
{
    var systemPrompt = promptVariant switch
    {
        "concise" => category switch
        {
            ScorerExecutors.PopularityId => PromptVariants.GetPopularityPrompt("concise"),
            ScorerExecutors.ArtisticValueId => PromptVariants.GetArtisticValuePrompt("concise"),
            ScorerExecutors.IconicnessId => PromptVariants.GetIconicnessPrompt("concise"),
            _ => ScorerExecutors.GetSystemPrompt(category)
        },
        _ => ScorerExecutors.GetSystemPrompt(category)
    };

    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, $"Evaluate the movie: {movieTitle}")
    };

    var options = new ChatOptions { Temperature = temperature };

    try
    {
        var response = await chatClient.GetResponseAsync<ScorerLlmOutput>(messages, options);

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
    catch (OperationCanceledException) { throw; }
    catch { /* structured output failed */ }

    return new ScorerResult
    {
        Category = category,
        Score = 50,
        Pros = [$"Could not evaluate {category} — defaulting to 50"],
        Cons = ["Structured output parsing failed"],
    };
}

static string SanitizeFileName(string name) =>
    string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

// ── Types ────────────────────────────────────────────────────────────────────
record ScorerLlmOutput(int Score, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons);

record SingleRunResult
{
    public required string Model { get; init; }
    public required string PromptVariant { get; init; }
    public required float Temperature { get; init; }
    public required string Movie { get; init; }
    public required int RunIndex { get; init; }
    public required int Score { get; init; }
    public int? PopularityScore { get; init; }
    public int? ArtisticValueScore { get; init; }
    public int? IconicnessScore { get; init; }
    public required double LatencySeconds { get; init; }
    public string? Error { get; init; }
}

record QualityEvalResult
{
    public required string Model { get; init; }
    public required string Movie { get; init; }
    public Dictionary<string, double> Metrics { get; init; } = new();
    public string? Error { get; init; }
}
