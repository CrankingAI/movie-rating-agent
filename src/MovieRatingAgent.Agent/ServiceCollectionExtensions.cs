using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MovieRatingAgent.Agent;

public class AgentOptions
{
    public string ModelId { get; set; } = "gpt-5.4";
    public float? Temperature { get; set; }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMovieGreatnessAgent(
        this IServiceCollection services,
        Uri foundryEndpoint,
        string apiKey,
        string modelId = "gpt-5.4")
    {
        var azureClient = new AzureOpenAIClient(foundryEndpoint, new AzureKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(modelId).AsIChatClient();

        services.AddChatClient(sp => new ChatClientBuilder(chatClient)
            .UseOpenTelemetry(configure: otel =>
            {
                otel.EnableSensitiveData = false;
            })
            .UseFunctionInvocation()
            .Build(sp));

        services.AddSingleton(new AgentOptions { ModelId = modelId });
        services.AddSingleton<MovieGreatnessAgent>();

        return services;
    }
}
