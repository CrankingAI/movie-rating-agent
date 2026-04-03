using System.Text.Json;
using MovieRatingAgent.Core.Models;

namespace MovieRatingAgent.Tests.Core.Models;

public class JobResponseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void JobResponse_RoundTrips_ThroughJson()
    {
        var response = new JobResponse { RequestedMovie = "The Godfather", RatedMovie = "The Godfather", Score = 95, Reasoning = "A masterpiece." };
        var json = JsonSerializer.Serialize(response, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<JobResponse>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(95, deserialized.Score);
        Assert.Equal("A masterpiece.", deserialized.Reasoning);
    }

    [Fact]
    public void JobResponse_WithSubScores_RoundTrips_ThroughJson()
    {
        var response = new JobResponse
        {
            RequestedMovie = "Test",
            RatedMovie = "Test",
            Score = 82,
            Reasoning = "Weighted score",
            SubScores = new SubScores
            {
                Popularity = 90,
                ArtisticValue = 85,
                Iconicness = 70,
            },
            Pros = ["Great acting", "Iconic scenes"],
            Cons = ["Slow pacing"],
            Conflicts = ["Critics vs audience divide"],
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<JobResponse>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(82, deserialized.Score);
        Assert.NotNull(deserialized.SubScores);
        Assert.Equal(90, deserialized.SubScores.Popularity);
    }

    [Fact]
    public void JobResponse_NullableFields_DefaultToNull()
    {
        var response = new JobResponse { RequestedMovie = "Test", RatedMovie = "Test", Score = 50, Reasoning = "Basic" };

        Assert.Null(response.SubScores);
        Assert.Null(response.Pros);
        Assert.Null(response.Cons);
        Assert.Null(response.Conflicts);
    }
}
