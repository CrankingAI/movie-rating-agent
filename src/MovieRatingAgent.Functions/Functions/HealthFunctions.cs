using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using MovieRatingAgent.Core;

namespace MovieRatingAgent.Functions.Functions;

public class HealthFunctions
{
    [Function("Readyz")]
    public async Task<HttpResponseData> Readyz(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "readyz")] HttpRequestData req,
        CancellationToken ct)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "ready",
            version = AgentVersion.Current,
            commit = AgentVersion.CommitHash,
        }, ct);
        return response;
    }

    [Function("Livez")]
    public async Task<HttpResponseData> Livez(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "livez")] HttpRequestData req,
        CancellationToken ct)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "alive",
            quote = "It's alive! It's alive!",
            attribution = "Henry Frankenstein, Frankenstein (1931)",
        }, ct);
        return response;
    }
}
