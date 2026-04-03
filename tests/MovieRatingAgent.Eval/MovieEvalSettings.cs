namespace MovieRatingAgent.Eval;

public sealed record MovieScoreExpectation(string Title, int MinScore, int MaxScore, int MaxSpread);

public static class MovieEvalSettings
{
    public static TheoryData<MovieScoreExpectation> GetScoreExpectations()
    {
        return new TheoryData<MovieScoreExpectation>
        {
            new("The Godfather", 98, 99, 3),
            new("Santa Claus Conquers the Martians", 12, 16, 5),
            new("To Kill a Mockingbird", 94, 96, 2)
        };
    }

    public static int GetRunCount()
    {
        return TryGetInt("MOVIE_EVAL_RUNS", out var runs) && runs > 0 ? runs : 7;
    }

    public static int GetRequiredInRangeCount(int runs)
    {
        if (TryGetInt("MOVIE_EVAL_REQUIRED_IN_RANGE_COUNT", out var requiredCount))
        {
            return Math.Clamp(requiredCount, 1, runs);
        }

        var requiredRate = TryGetDouble("MOVIE_EVAL_REQUIRED_IN_RANGE_RATE", out var rate)
            ? Math.Clamp(rate, 0.0, 1.0)
            : 0.70;

        return Math.Clamp((int)Math.Ceiling(runs * requiredRate), 1, runs);
    }

    private static bool TryGetInt(string key, out int value)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(key), out value);
    }

    private static bool TryGetDouble(string key, out double value)
    {
        return double.TryParse(Environment.GetEnvironmentVariable(key), out value);
    }
}
