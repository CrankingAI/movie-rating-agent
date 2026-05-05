namespace MovieRatingAgent.Agent.Progress;

/// <summary>
/// Receives per-step lifecycle events as the agent progresses through title
/// resolution and the workflow executors. Implementations typically persist
/// state somewhere the client can poll (e.g. <c>meta.json</c> in blob storage).
/// </summary>
public interface IProgressSink
{
    Task MarkStartedAsync(string stepName, CancellationToken ct = default);
    Task MarkCompletedAsync(string stepName, CancellationToken ct = default);
    Task MarkFailedAsync(string stepName, string? error, CancellationToken ct = default);
}

/// <summary>No-op sink used when the caller does not care about progress events.</summary>
public sealed class NullProgressSink : IProgressSink
{
    public static readonly NullProgressSink Instance = new();

    private NullProgressSink() { }

    public Task MarkStartedAsync(string stepName, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkCompletedAsync(string stepName, CancellationToken ct = default) => Task.CompletedTask;
    public Task MarkFailedAsync(string stepName, string? error, CancellationToken ct = default) => Task.CompletedTask;
}
