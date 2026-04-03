using System.Text.Json;
using MovieRatingAgent.Core.Models;

namespace MovieRatingAgent.Tests.Core.Models;

public class JobRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void JobRequest_RoundTrips_ThroughJson()
    {
        var request = new JobRequest { Topic = "The Godfather" };
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<JobRequest>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("The Godfather", deserialized.Topic);
    }

    [Fact]
    public void JobRequest_Deserializes_FromCamelCase()
    {
        var json = """{"topic":"Citizen Kane"}""";
        var request = JsonSerializer.Deserialize<JobRequest>(json, JsonOptions);

        Assert.NotNull(request);
        Assert.Equal("Citizen Kane", request.Topic);
    }
}
