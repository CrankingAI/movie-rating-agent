using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MovieRatingAgent.Agent;
using MovieRatingAgent.Core.Services;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Wire the worker invocation pipeline through OpenTelemetry. Without this,
// ActivitySource spans created during a function invocation (the
// Microsoft.Extensions.AI gen_ai chat spans, scorer activities, agent workflow,
// HttpClient calls to Azure OpenAI, etc.) are NOT captured by the worker's
// OTel exporters — only startup events and the gRPC stream show up.
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

var storageConnectionString = builder.Configuration["AzureWebJobsStorage"]
    ?? "UseDevelopmentStorage=true";

builder.Services.AddSingleton(_ => new BlobServiceClient(storageConnectionString));
builder.Services.AddSingleton(_ => new QueueServiceClient(storageConnectionString, new QueueClientOptions
{
    MessageEncoding = QueueMessageEncoding.Base64
}));
builder.Services.AddSingleton<JobBlobService>();
builder.Services.AddSingleton<JobQueueService>();

var foundryEndpoint = builder.Configuration["Foundry:Endpoint"]
    ?? throw new InvalidOperationException("Foundry:Endpoint configuration is required");
var foundryApiKey = builder.Configuration["Foundry:ApiKey"]
    ?? throw new InvalidOperationException("Foundry:ApiKey configuration is required");
var foundryModelId = builder.Configuration["Foundry:ModelId"] ?? "gpt-5.4";

builder.Services.AddMovieGreatnessAgent(
    new Uri(foundryEndpoint),
    foundryApiKey,
    foundryModelId);

builder.Build().Run();
