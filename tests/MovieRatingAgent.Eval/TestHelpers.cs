using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace MovieRatingAgent.Eval;

internal static class TestHelpers
{
    internal static IChatClient CreateChatClient(string? modelId = null, float? temperature = null)
    {
        var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT")
            ?? throw new InvalidOperationException("FOUNDRY_ENDPOINT env var required");
        var apiKey = Environment.GetEnvironmentVariable("FOUNDRY_API_KEY")
            ?? throw new InvalidOperationException("FOUNDRY_API_KEY env var required");
        modelId ??= Environment.GetEnvironmentVariable("FOUNDRY_MODEL_ID") ?? "gpt-5.4";

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        return azureClient.GetChatClient(modelId).AsIChatClient();
    }

    internal static MovieRatingAgent.Agent.MovieGreatnessAgent CreateAgent(string? modelId = null, float? temperature = null)
    {
        modelId ??= Environment.GetEnvironmentVariable("FOUNDRY_MODEL_ID") ?? "gpt-5.4";
        var chatClient = CreateChatClient(modelId, temperature);
        var pipeline = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation()
            .Build();

        return new MovieRatingAgent.Agent.MovieGreatnessAgent(pipeline,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MovieRatingAgent.Agent.MovieGreatnessAgent>.Instance,
            new MovieRatingAgent.Agent.AgentOptions { ModelId = modelId, Temperature = temperature });
    }
}
