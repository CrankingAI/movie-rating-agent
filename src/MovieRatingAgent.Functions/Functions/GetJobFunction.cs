using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using MovieRatingAgent.Core.Models;
using MovieRatingAgent.Core.Observability;
using MovieRatingAgent.Core.Services;

namespace MovieRatingAgent.Functions.Functions;

public class GetJobFunction
{
    private static readonly ActivitySource ActivitySourceInstance = new("MovieRatingAgent.Agent");

    private readonly JobBlobService _blobService;

    public GetJobFunction(JobBlobService blobService)
    {
        _blobService = blobService;
    }

    [Function("GetJob")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "jobs/{jobId}")] HttpRequestData req,
        FunctionContext executionContext,
        string jobId,
        CancellationToken ct)
    {
        using var activity = StartActivityFromContext(executionContext, "GetJob", ActivityKind.Internal);
        activity?.SetTag(TelemetryTags.JobId, jobId);

        var meta = await _blobService.ReadMetaAsync(jobId, ct);
        if (meta is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Job not found" }, ct);
            return notFound;
        }

        activity?.SetTag(TelemetryTags.JobStatus, meta.Status.ToString());

        JobResponse? result = null;
        if (meta.Status == JobStatus.Completed)
        {
            result = await _blobService.ReadResponseAsync(jobId, ct);
            if (result is not null)
            {
                activity?.SetTag(TelemetryTags.MovieRequested, result.RequestedMovie);
                activity?.SetTag(TelemetryTags.MovieRated, result.RatedMovie);
                activity?.SetTag(TelemetryTags.MovieScore, result.Score);
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { meta, result }, ct);
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
