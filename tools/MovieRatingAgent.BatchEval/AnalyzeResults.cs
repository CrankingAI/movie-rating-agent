using System.Text.Json;
using System.Text.Json.Serialization;
using MovieRatingAgent.BatchEval;

namespace MovieRatingAgent.BatchEval;

/// <summary>
/// Reads existing eval-results JSON files and produces the analysis.json + console summary.
/// Run with: dotnet run --project tools/MovieRatingAgent.BatchEval -- --analyze-only
/// </summary>
public static class AnalyzeResults
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public record RunResult
    {
        public string Model { get; init; } = "";
        public string PromptVariant { get; init; } = "";
        public float Temperature { get; init; }
        public string Movie { get; init; } = "";
        public int RunIndex { get; init; }
        public int Score { get; init; }
        public int? PopularityScore { get; init; }
        public int? ArtisticValueScore { get; init; }
        public int? IconicnessScore { get; init; }
        public double LatencySeconds { get; init; }
        public string? Error { get; init; }
    }

    public static async Task RunAnalysis(string resultsDir)
    {
        Console.WriteLine($"==> Analyzing results in {resultsDir}");

        // Load all run results
        var allResults = new List<RunResult>();
        foreach (var file in Directory.GetFiles(resultsDir, "*.json"))
        {
            if (Path.GetFileName(file) is "analysis.json" or "quality_eval.json")
                continue;

            var json = await File.ReadAllTextAsync(file);
            var results = JsonSerializer.Deserialize<List<RunResult>>(json, JsonOpts);
            if (results is not null)
                allResults.AddRange(results);
        }

        Console.WriteLine($"    Loaded {allResults.Count} run results from {Directory.GetFiles(resultsDir, "*.json").Length - 1} files");

        var analysisResults = new Dictionary<string, object>();

        // Per-combination descriptive stats
        var comboStats = new List<object>();
        foreach (var combo in allResults
            .GroupBy(r => $"{r.Model}|{r.PromptVariant}|t={r.Temperature}|{r.Movie}")
            .OrderBy(g => g.Key))
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
            var byModel = group.GroupBy(r => r.Model)
                .ToDictionary(g => g.Key, g => g.Select(r => (double)r.Score).ToList());
            var models = byModel.Keys.OrderBy(k => k).ToList();

            for (var i = 0; i < models.Count; i++)
            for (var j = i + 1; j < models.Count; j++)
            {
                if (byModel[models[i]].Count < 2 || byModel[models[j]].Count < 2) continue;
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
            var byPrompt = group.GroupBy(r => r.PromptVariant)
                .ToDictionary(g => g.Key, g => g.Select(r => (double)r.Score).ToList());
            if (byPrompt.Count >= 2)
            {
                var prompts = byPrompt.Keys.OrderBy(k => k).ToList();
                if (byPrompt[prompts[0]].Count < 2 || byPrompt[prompts[1]].Count < 2) continue;
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
            var byTemp = group.GroupBy(r => r.Temperature)
                .ToDictionary(g => $"t={g.Key}", g => g.Select(r => (double)r.Score).ToList());
            if (byTemp.Count >= 2)
            {
                var temps = byTemp.Keys.OrderBy(k => k).ToList();
                if (byTemp[temps[0]].Count < 2 || byTemp[temps[1]].Count < 2) continue;
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

        // Load quality eval if exists
        var qualityPath = Path.Combine(resultsDir, "quality_eval.json");
        if (File.Exists(qualityPath))
        {
            var qualityJson = await File.ReadAllTextAsync(qualityPath);
            var qualityData = JsonSerializer.Deserialize<JsonElement>(qualityJson);
            analysisResults["quality_eval"] = qualityData;
        }

        // Write analysis
        await File.WriteAllTextAsync(
            Path.Combine(resultsDir, "analysis.json"),
            JsonSerializer.Serialize(analysisResults, JsonOpts));
        Console.WriteLine($"    Wrote analysis.json");

        // Print summary
        Console.WriteLine();
        Console.WriteLine("  Model               Prompt    Temp    Movie                           Mean   StdDev  CI95             Range");
        Console.WriteLine("  ─────               ──────    ────    ─────                           ────   ──────  ────             ─────");
        foreach (var stat in comboStats.Cast<dynamic>())
        {
            var ss = (StatisticalAnalysis.DescriptiveStats)stat.ScoreStats;
            Console.WriteLine($"  {stat.Model,-20} {stat.PromptVariant,-9} {stat.Temperature,-7} {stat.Movie,-30} {ss.Mean,6:F1} {ss.StdDev,7:F2}  [{ss.CI95Lower:F1}, {ss.CI95Upper:F1}]  [{ss.Min}-{ss.Max}]");
        }

        Console.WriteLine();
        Console.WriteLine($"  Significant model comparisons (p < 0.05): {modelComparisons.Cast<dynamic>().Count(c => (bool)c.IsSignificant)} of {modelComparisons.Count}");
        foreach (var mc in modelComparisons.Cast<dynamic>().Where(c => (bool)c.IsSignificant).Take(20))
        {
            Console.WriteLine($"    {mc.Context}: {mc.GroupA} vs {mc.GroupB} — d={mc.CohensD:F2} ({mc.EffectSizeLabel}), p={mc.MannWhitneyP:F4}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Significant prompt comparisons (p < 0.05): {promptComparisons.Cast<dynamic>().Count(c => (bool)c.IsSignificant)} of {promptComparisons.Count}");
        foreach (var pc in promptComparisons.Cast<dynamic>().Where(c => (bool)c.IsSignificant).Take(10))
        {
            Console.WriteLine($"    {pc.Context}: {pc.GroupA} vs {pc.GroupB} — d={pc.CohensD:F2} ({pc.EffectSizeLabel}), p={pc.MannWhitneyP:F4}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Significant temperature comparisons (p < 0.05): {tempComparisons.Cast<dynamic>().Count(c => (bool)c.IsSignificant)} of {tempComparisons.Count}");
        foreach (var tc in tempComparisons.Cast<dynamic>().Where(c => (bool)c.IsSignificant).Take(10))
        {
            Console.WriteLine($"    {tc.Context}: {tc.GroupA} vs {tc.GroupB} — d={tc.CohensD:F2} ({tc.EffectSizeLabel}), p={tc.MannWhitneyP:F4}");
        }
    }
}
