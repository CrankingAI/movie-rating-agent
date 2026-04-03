namespace MovieRatingAgent.Eval;

public sealed class RequiresFoundryFact : FactAttribute
{
    public RequiresFoundryFact()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FOUNDRY_API_KEY")))
        {
            Skip = "Requires FOUNDRY_ENDPOINT and FOUNDRY_API_KEY env vars";
        }
    }
}

public sealed class RequiresFoundryTheory : TheoryAttribute
{
    public RequiresFoundryTheory()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FOUNDRY_API_KEY")))
        {
            Skip = "Requires FOUNDRY_ENDPOINT and FOUNDRY_API_KEY env vars";
        }
    }
}
