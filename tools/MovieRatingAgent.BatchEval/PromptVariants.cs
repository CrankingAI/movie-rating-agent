namespace MovieRatingAgent.BatchEval;

/// <summary>
/// Prompt variants for A/B testing scorer prompts.
/// "detailed" = original verbose prompts. "concise" = stripped-down direct prompts.
/// </summary>
public static class PromptVariants
{
    public static string GetPopularityPrompt(string variant) => variant switch
    {
        "detailed" =>
            """
            You are a movie popularity analyst. Given a movie name, evaluate its POPULARITY
            on a scale from 0 (completely unknown) to 100 (universally known blockbuster).
            Consider box office performance, audience reach, streaming popularity, and
            mainstream recognition. Provide specific PROS (factors boosting popularity)
            and CONS (factors limiting popularity).
            """,
        "concise" =>
            """
            Rate this movie's POPULARITY from 0-100. Consider box office, streaming numbers,
            and mainstream awareness. List PROS and CONS.
            """,
        _ => throw new ArgumentException($"Unknown prompt variant: {variant}")
    };

    public static string GetArtisticValuePrompt(string variant) => variant switch
    {
        "detailed" =>
            """
            You are an expert film critic focused on artistic merit. Given a movie name,
            evaluate its ARTISTIC VALUE on a scale from 0 (no artistic merit) to 100
            (a perfect artistic achievement). Consider cinematography, direction, acting,
            screenplay, editing, and whether it won or was nominated for major awards like
            the Academy Award for Best Picture. Provide specific PROS (artistic strengths)
            and CONS (artistic weaknesses).
            """,
        "concise" =>
            """
            Rate this movie's ARTISTIC VALUE from 0-100. Consider direction, acting,
            cinematography, and awards. List PROS and CONS.
            """,
        _ => throw new ArgumentException($"Unknown prompt variant: {variant}")
    };

    public static string GetIconicnessPrompt(string variant) => variant switch
    {
        "detailed" =>
            """
            You are a cultural historian specializing in cinema. Given a movie name,
            evaluate its ICONICNESS on a scale from 0 (no cultural footprint) to 100
            (deeply embedded in global culture). Consider memorable quotes
            (e.g. "I'm gonna make him an offer he can't refuse", "May the Force be with you",
            "these go to eleven"), iconic scenes, influence on other films, parodies and
            references in popular culture, and lasting cultural impact. Provide specific
            PROS (iconic elements) and CONS (factors limiting cultural impact).
            """,
        "concise" =>
            """
            Rate this movie's ICONICNESS from 0-100. Consider famous quotes, iconic scenes,
            cultural impact, and influence on other films. List PROS and CONS.
            """,
        _ => throw new ArgumentException($"Unknown prompt variant: {variant}")
    };
}
