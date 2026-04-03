using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MovieRatingAgent.Core;
using MovieRatingAgent.Core.Models;
using MovieRatingAgent.Core.Observability;
using MovieRatingAgent.Core.Services;

namespace MovieRatingAgent.Functions.Functions;

public class SubmitJobFunction
{
    private static readonly ActivitySource ActivitySourceInstance = new("MovieRatingAgent.Agent");

    private readonly JobBlobService _blobService;
    private readonly JobQueueService _queueService;
    private readonly ILogger<SubmitJobFunction> _logger;

    public SubmitJobFunction(
        JobBlobService blobService,
        JobQueueService queueService,
        ILogger<SubmitJobFunction> logger)
    {
        _blobService = blobService;
        _queueService = queueService;
        _logger = logger;
    }

    [Function("SubmitJob")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs")] HttpRequestData req,
        FunctionContext executionContext,
        CancellationToken ct)
    {
        using var activity = StartActivityFromContext(executionContext, "SubmitJob", ActivityKind.Internal);

        var jobRequest = await req.ReadFromJsonAsync<JobRequest>(ct);
        if (jobRequest is null || string.IsNullOrWhiteSpace(jobRequest.Topic))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Request must include a 'topic' field" }, ct);
            return badRequest;
        }

        var jobId = Guid.NewGuid().ToString("N");
        activity?.SetTag(TelemetryTags.JobId, jobId);
        activity?.SetTag(TelemetryTags.MovieRequested, jobRequest.Topic);

        await _blobService.EnsureContainerExistsAsync(ct);
        await _blobService.WriteRequestAsync(jobId, jobRequest, ct);

        var meta = new JobMeta
        {
            JobId = jobId,
            Status = JobStatus.Queued,
            AgentVersion = AgentVersion.Current,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _blobService.WriteMetaAsync(jobId, meta, ct);

        await _queueService.EnsureQueueExistsAsync(ct);
        await _queueService.EnqueueJobAsync(jobId, ct);

        _logger.LogInformation(
            "Job {JobId} submitted for requested movie {RequestedMovie}",
            jobId,
            jobRequest.Topic);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { jobId }, ct);
        return response;
    }

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
