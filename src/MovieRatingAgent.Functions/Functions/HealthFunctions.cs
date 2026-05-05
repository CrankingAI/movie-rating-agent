using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    private static readonly JsonSerializerOptions _relaxedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Function("Livez")]
    public async Task<HttpResponseData> Livez(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "livez")] HttpRequestData req,
        CancellationToken ct)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var json = JsonSerializer.Serialize(new
        {
            status = "alive",
            quote = "It's alive! It's alive!",
            attribution = "Henry Frankenstein, Frankenstein (1931)",
        }, _relaxedJsonOptions);
        await response.WriteStringAsync(json, ct);
        return response;
    }
}
