using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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

        services.AddChatClient(sp =>
        {
            // Honor the OpenTelemetry GenAI semantic-convention env var for capturing
            // prompts and completions on chat spans. Default OFF to avoid logging PII;
            // set OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true (or
            // GENAI_CAPTURE_MESSAGE_CONTENT=true for short-form) to enable.
            var config = sp.GetRequiredService<IConfiguration>();
            var captureMessages =
                string.Equals(config["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"], "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(config["GENAI_CAPTURE_MESSAGE_CONTENT"], "true", StringComparison.OrdinalIgnoreCase);

            return new ChatClientBuilder(chatClient)
                .UseOpenTelemetry(configure: otel =>
                {
                    otel.EnableSensitiveData = captureMessages;
                })
                .UseFunctionInvocation()
                .Build(sp);
        });

        services.AddSingleton(new AgentOptions { ModelId = modelId });
        services.AddSingleton<MovieGreatnessAgent>();

        return services;
    }
}
