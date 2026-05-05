// =============================================================================
// Aspire AppHost — local-dev orchestration for the Movie Rating Agent.
//
// What this gives you:
//   * Azurite (storage emulator) for queue + blob persistence.
//   * An OpenTelemetry Collector that fans incoming OTLP traffic out to a
//     local file (./otel-export/*.jsonl) so you can grep/diff offline.
//   * The Functions worker, with all secrets piped from user-secrets and the
//     OTLP endpoint pointed at the Aspire dashboard.
//
// Aspire automatically injects OTEL_EXPORTER_OTLP_ENDPOINT for every resource
// it manages, so the Functions worker reports gen_ai spans (via Microsoft.
// Extensions.AI.UseOpenTelemetry) directly to the Aspire dashboard at
// https://localhost:15888 — no extra wiring required.
// =============================================================================

var builder = DistributedApplication.CreateBuilder(args);

// ── Foundry credentials (see scripts/set-local-creds.sh) ──────────────────
var foundryEndpoint = builder.AddParameter("foundry-endpoint", secret: false);
var foundryApiKey   = builder.AddParameter("foundry-apikey", secret: true);
var foundryModelId  = builder.AddParameter("foundry-modelid", secret: false);

// ── Azurite (queue + blob emulator) ───────────────────────────────────────
// Persistent lifetime keeps the container warm across Aspire restarts so
// dev iteration is fast even if Docker Desktop went into Resource Saver mode.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithLifetime(ContainerLifetime.Persistent));

var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");

// ── OTel Collector → ./otel-export/*.jsonl ────────────────────────────────
// Receives OTLP-HTTP and writes traces/metrics/logs to local files for
// offline inspection. The Aspire dashboard is the primary UX; this file
// export is the "diff against last run" tool.
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHttpEndpoint(targetPort: 4318, name: "otlp-http")
    .WithBindMount("../../otel-collector-config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithBindMount("../../otel-export", "/otel-export");

// ── Functions worker ──────────────────────────────────────────────────────
// Honor `GENAI_CAPTURE_MESSAGE_CONTENT=true` from the host shell to opt into
// capturing prompt/completion text on the gen_ai chat span. Default OFF.
var captureGenAiContent = Environment.GetEnvironmentVariable("GENAI_CAPTURE_MESSAGE_CONTENT") ?? "false";

builder.AddAzureFunctionsProject<Projects.MovieRatingAgent_Functions>("functions")
    .WithHostStorage(storage)
    .WithReference(blobs)
    .WithReference(queues)
    .WithEnvironment("Foundry__Endpoint", foundryEndpoint)
    .WithEnvironment("Foundry__ApiKey", foundryApiKey)
    .WithEnvironment("Foundry__ModelId", foundryModelId)
    .WithEnvironment("OTEL_FILE_EXPORTER_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OTEL_SERVICE_NAME", "func-movie-rating-agent-local")
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "service.namespace=movie-rating-agent,deployment.environment=local")
    .WithEnvironment("GENAI_CAPTURE_MESSAGE_CONTENT", captureGenAiContent);

builder.Build().Run();
