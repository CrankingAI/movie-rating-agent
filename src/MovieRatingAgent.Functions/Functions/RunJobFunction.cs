using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MovieRatingAgent.Agent;
using MovieRatingAgent.Agent.Progress;
using MovieRatingAgent.Core;
using MovieRatingAgent.Core.Models;
using MovieRatingAgent.Core.Observability;
using MovieRatingAgent.Core.Services;
using MovieRatingAgent.Functions.Services;

namespace MovieRatingAgent.Functions.Functions;

public class RunJobFunction
{
    private static readonly ActivitySource ActivitySourceInstance = new("MovieRatingAgent.Agent");

    private readonly JobBlobService _blobService;
    private readonly MovieGreatnessAgent _agent;
    private readonly ILogger<RunJobFunction> _logger;

    public RunJobFunction(
        JobBlobService blobService,
        MovieGreatnessAgent agent,
        ILogger<RunJobFunction> logger)
    {
        _blobService = blobService;
        _agent = agent;
        _logger = logger;
    }

    [Function("RunJob")]
    public async Task Run(
        [QueueTrigger("job-requests", Connection = "AzureWebJobsStorage")] string jobId,
        FunctionContext executionContext,
        CancellationToken ct)
    {
        using var activity = StartActivityFromContext(executionContext, "RunJob", ActivityKind.Consumer);
        activity?.SetTag(TelemetryTags.JobId, jobId);

        _logger.LogInformation("Processing job {JobId}", jobId);

        var request = await _blobService.ReadRequestAsync(jobId, ct);
        if (request is null)
        {
            _logger.LogError("Job {JobId}: request.json not found", jobId);
            return;
        }

        var meta = await _blobService.ReadMetaAsync(jobId, ct);
        if (meta is null)
        {
            _logger.LogError("Job {JobId}: meta.json not found", jobId);
            return;
        }

        activity?.SetTag(TelemetryTags.MovieRequested, request.Topic);

        meta = meta with
        {
            Status = JobStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Progress = SeedProgressSteps(),
        };
        await _blobService.WriteMetaAsync(jobId, meta, ct);

        await using var sink = new JobMetaProgressSink(_blobService, jobId, meta);

        try
        {
            var result = await _agent.RunAsync(request, sink, ct);
            await _blobService.WriteResponseAsync(jobId, result, ct);

            var terminal = sink.Snapshot with
            {
                Status = JobStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            await sink.ReplaceAndFlushAsync(terminal, ct);

            activity?.SetTag(TelemetryTags.JobStatus, "Completed");
            activity?.SetTag(TelemetryTags.MovieRequested, result.RequestedMovie);
            activity?.SetTag(TelemetryTags.MovieRated, result.RatedMovie);
            activity?.SetTag(TelemetryTags.MovieScore, result.Score);

            _logger.LogInformation(
                "Job {JobId} completed for requested movie {RequestedMovie} and rated movie {RatedMovie}. Score: {Score}",
                jobId,
                result.RequestedMovie,
                result.RatedMovie,
                result.Score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed for requested movie {RequestedMovie}", jobId, request.Topic);

            activity?.SetTag(TelemetryTags.JobStatus, "Failed");
            activity?.SetTag(TelemetryTags.MovieRequested, request.Topic);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var terminal = sink.Snapshot with
            {
                Status = JobStatus.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = ex.Message,
            };
            await sink.ReplaceAndFlushAsync(terminal, ct);
        }
    }

    private static IReadOnlyList<ProgressStep> SeedProgressSteps()
        => ProgressSteps.Ordered
            .Select(s => new ProgressStep
            {
                Name = s.Name,
                Label = s.Label,
                State = ProgressState.Pending,
            })
            .ToList();

    private static Activity? StartActivityFromContext(
        FunctionContext context, string name, ActivityKind kind)
    {
        var traceParent = context.TraceContext.TraceParent;
        if (!string.IsNullOrEmpty(traceParent))
        {
            var parentContext = ActivityContext.Parse(traceParent, context.TraceContext.TraceState);
            return ActivitySourceInstance.StartActivity(name, kind, parentContext);
        }
        return ActivitySourceInstance.StartActivity(name, kind);
    }
}
