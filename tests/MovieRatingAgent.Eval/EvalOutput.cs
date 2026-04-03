using Xunit.Abstractions;

namespace MovieRatingAgent.Eval;

internal static class EvalOutput
{
    internal static void WriteLine(ITestOutputHelper output, string message)
    {
        output.WriteLine(message);
    }
}
