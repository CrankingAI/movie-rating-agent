var builder = DistributedApplication.CreateBuilder(args);

var foundryEndpoint = builder.AddParameter("foundry-endpoint", secret: false);
var foundryApiKey = builder.AddParameter("foundry-apikey", secret: true);
var foundryModelId = builder.AddParameter("foundry-modelid", secret: false);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");

// OTel Collector — receives OTLP and writes structured JSONL to ./otel-export/
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithHttpEndpoint(targetPort: 4318, name: "otlp-http")
    .WithBindMount("../../otel-collector-config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithBindMount("../../otel-export", "/otel-export");

builder.AddAzureFunctionsProject<Projects.MovieRatingAgent_Functions>("functions")
    .WithHostStorage(storage)
    .WithReference(blobs)
    .WithReference(queues)
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("Foundry__ModelId", foundryModelId)
    .WithEnvironment("OTEL_FILE_EXPORTER_ENDPOINT", otelCollector.GetEndpoint("otlp-http"));

builder.Build().Run();
