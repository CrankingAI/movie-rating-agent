using Microsoft.Extensions.AI;
using MovieRatingAgent.Agent;

// A no-op chat client — the workflow is never executed, just inspected for its graph shape.
var nullClient = new NullChatClient();
var mermaid = MovieGreatnessAgent.GetWorkflowMermaid(nullClient);

var repoRoot = FindRepoRoot() ?? ".";
var mmdPath = Path.Combine(repoRoot, "agent-workflow.mmd");
File.WriteAllText(mmdPath, mermaid);
Console.WriteLine($"Wrote {mmdPath}");
Console.WriteLine();
Console.WriteLine(mermaid);

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "MovieRatingAgent.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

/// <summary>
/// Minimal IChatClient that is never called — satisfies the type system for workflow construction.
/// </summary>
sealed class NullChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("null");
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("NullChatClient is for graph inspection only.");
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("NullChatClient is for graph inspection only.");
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
