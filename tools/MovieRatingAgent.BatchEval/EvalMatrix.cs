namespace MovieRatingAgent.BatchEval;

/// <summary>
/// Defines a single evaluation combination: model × prompt variant × temperature × movie.
/// </summary>
public record EvalCombination(
    string ModelId,
    string PromptVariant,
    float Temperature,
    string Movie);

/// <summary>
/// Defines the full evaluation matrix.
/// </summary>
public static class EvalMatrix
{
    public static readonly string[] Models = ["gpt-5.4", "gpt-4o", "gpt-4o-mini"];

    public static readonly string[] PromptVariants = ["detailed", "concise"];

    public static readonly float[] Temperatures = [0.0f, 0.7f];

    public static readonly (string Title, int ExpectedMin, int ExpectedMax)[] Movies =
    [
        ("The Godfather", 85, 100),
        ("Santa Claus Conquers the Martians", 5, 25),
        ("Citizen Kane", 85, 100),
    ];

    public static int RunsPerCombination => 100;

    public static IEnumerable<EvalCombination> GetAllCombinations()
    {
        foreach (var model in Models)
        foreach (var prompt in PromptVariants)
        foreach (var temp in Temperatures)
        foreach (var (title, _, _) in Movies)
        {
            yield return new EvalCombination(model, prompt, temp, title);
        }
    }

    public static int TotalCombinations =>
        Models.Length * PromptVariants.Length * Temperatures.Length * Movies.Length;

    public static int TotalRuns => TotalCombinations * RunsPerCombination;
}
