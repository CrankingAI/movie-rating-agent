using MovieRatingAgent.Agent.Progress;
using MovieRatingAgent.Core.Models;
using MovieRatingAgent.Core.Services;

namespace MovieRatingAgent.Functions.Services;

/// <summary>
/// <see cref="IProgressSink"/> that mutates an in-memory <see cref="JobMeta"/>
/// snapshot and writes it to blob storage. Writes are coalesced (light debounce
/// via a serialized timer) so a flurry of executor events from a single superstep
/// turns into ~1 blob write rather than 3+. Always flushes on terminal states
/// (Completed / Failed) so the final state lands immediately.
/// </summary>
public sealed class JobMetaProgressSink : IProgressSink, IAsyncDisposable
{
    private readonly JobBlobService _blobService;
    private readonly string _jobId;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private JobMeta _snapshot;
    private bool _dirty;
    private DateTimeOffset _lastFlushedAt;

    public JobMetaProgressSink(
        JobBlobService blobService,
        string jobId,
        JobMeta initial,
        TimeSpan? debounce = null)
    {
        _blobService = blobService;
        _jobId = jobId;
        _snapshot = initial;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        _lastFlushedAt = DateTimeOffset.MinValue;
    }

    /// <summary>The current in-memory meta. Callers can use this for terminal writes
    /// (e.g., setting Status=Completed) so they layer on top of progress updates
    /// instead of racing them.</summary>
    public JobMeta Snapshot
    {
        get
        {
            _gate.Wait();
            try { return _snapshot; }
            finally { _gate.Release(); }
        }
    }

    public Task MarkStartedAsync(string stepName, CancellationToken ct = default)
        => UpdateAsync(stepName, s => s with { State = ProgressState.Running, StartedAt = DateTimeOffset.UtcNow }, terminal: false, ct);

    public Task MarkCompletedAsync(string stepName, CancellationToken ct = default)
        => UpdateAsync(stepName, s => s with { State = ProgressState.Completed, CompletedAt = DateTimeOffset.UtcNow }, terminal: false, ct);

    public Task MarkFailedAsync(string stepName, string? error, CancellationToken ct = default)
        => UpdateAsync(stepName, s => s with { State = ProgressState.Failed, CompletedAt = DateTimeOffset.UtcNow }, terminal: true, ct);

    /// <summary>Force a flush. Call before writing terminal status so the persisted
    /// meta reflects every step that fired before the workflow finished.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_dirty)
            {
                await _blobService.WriteMetaAsync(_jobId, _snapshot, ct);
                _dirty = false;
                _lastFlushedAt = DateTimeOffset.UtcNow;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Replace the in-memory snapshot wholesale (used by the caller to
    /// merge in Status / CompletedAt / Error after the agent finishes) and flush.</summary>
    public async Task ReplaceAndFlushAsync(JobMeta newSnapshot, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _snapshot = newSnapshot;
            await _blobService.WriteMetaAsync(_jobId, _snapshot, ct);
            _dirty = false;
            _lastFlushedAt = DateTimeOffset.UtcNow;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync(CancellationToken.None);
        _gate.Dispose();
    }

    private async Task UpdateAsync(string stepName, Func<ProgressStep, ProgressStep> mutate, bool terminal, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var steps = _snapshot.Progress.ToList();
            var idx = steps.FindIndex(s => s.Name == stepName);
            if (idx < 0)
            {
                // Step not pre-seeded — ignore silently (e.g. an internal executor
                // we forgot to filter, or a future workflow change).
                return;
            }

            steps[idx] = mutate(steps[idx]);
            _snapshot = _snapshot with { Progress = steps };
            _dirty = true;

            var sinceLast = DateTimeOffset.UtcNow - _lastFlushedAt;
            if (terminal || sinceLast >= _debounce)
            {
                await _blobService.WriteMetaAsync(_jobId, _snapshot, ct);
                _dirty = false;
                _lastFlushedAt = DateTimeOffset.UtcNow;
            }
        }
        finally { _gate.Release(); }
    }
}
