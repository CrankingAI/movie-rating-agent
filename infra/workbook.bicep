// =============================================================================
// Azure Monitor workbook — pre-canned dashboard for the Movie Rating Agent.
//
// Drops a workbook into Application Insights with KQL queries that are
// already wired up to the OpenTelemetry GenAI semconv attributes the Functions
// worker emits. After deploy, find it under
//   Application Insights → Workbooks → "Movie Rating Agent — Gen AI"
// =============================================================================

@description('Application Insights resource name (the workbook scopes here).')
param appInsightsName string

@description('Azure region.')
param location string

@description('Tags applied to the workbook.')
param tags object = {}

// Stable GUID derived from the AI resource ID so re-deploys are idempotent.
var workbookId = guid(resourceGroup().id, appInsightsName, 'mra-genai-workbook')

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

var workbookContent = {
  version: 'Notebook/1.0'
  items: [
    {
      type: 1
      content: {
        json: '# Movie Rating Agent — Gen AI dashboard\n\nGen AI spans, latencies, model usage, and job outcomes for the Movie Rating Agent.\n\n* **Source:** OpenTelemetry GenAI semantic conventions emitted by Microsoft.Extensions.AI.\n* **Tip:** filter by `cloud_RoleName == "func-movie-rating-agent-dev"` to focus on deployed traffic.'
      }
    }
    {
      type: 9
      content: {
        version: 'KqlParameterItem/1.0'
        parameters: [
          {
            id: '00000000-0000-0000-0000-000000000001'
            version: 'KqlParameterItem/1.0'
            name: 'TimeRange'
            type: 4
            value: { durationMs: 86400000 }
            typeSettings: {
              selectableValues: [
                { durationMs: 3600000 }
                { durationMs: 14400000 }
                { durationMs: 86400000 }
                { durationMs: 604800000 }
              ]
            }
          }
        ]
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'dependencies\n| where name startswith "chat " or name startswith "invoke_agent " or name startswith "executor.process"\n| summarize calls = count(), avgMs = avg(duration), p95Ms = percentile(duration, 95), p99Ms = percentile(duration, 99) by name\n| order by calls desc'
        size: 0
        title: 'Gen AI / agent span latency'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
        visualization: 'table'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'dependencies\n| where name startswith "chat "\n| extend model = tostring(customDimensions["gen_ai.request.model"])\n| where isnotempty(model)\n| summarize calls = count(), avgMs = avg(duration) by model, bin(timestamp, 5m)\n| render timechart'
        size: 0
        title: 'Chat completion calls per model over time'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'dependencies\n| where name startswith "chat "\n| extend model = tostring(customDimensions["gen_ai.request.model"])\n| extend inTokens = tolong(customDimensions["gen_ai.usage.input_tokens"])\n| extend outTokens = tolong(customDimensions["gen_ai.usage.output_tokens"])\n| where isnotempty(model)\n| summarize calls = count(), p50ms = percentile(duration, 50), p95ms = percentile(duration, 95), inTokens = sum(inTokens), outTokens = sum(outTokens) by model, name\n| order by calls desc'
        size: 0
        title: 'Per-scorer chat completion latency + token usage'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
        visualization: 'table'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'dependencies\n| where name startswith "chat "\n| extend inTokens = tolong(customDimensions["gen_ai.usage.input_tokens"])\n| extend outTokens = tolong(customDimensions["gen_ai.usage.output_tokens"])\n| extend model = tostring(customDimensions["gen_ai.request.model"])\n| summarize totalIn = sum(inTokens), totalOut = sum(outTokens) by model, bin(timestamp, 15m)\n| render columnchart'
        size: 0
        title: 'Token usage per model over time'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'traces\n| where message has "Job" and (message has "completed" or message has "failed")\n| summarize completed = countif(message has "completed"), failed = countif(message has "failed") by bin(timestamp, 15m)\n| render columnchart'
        size: 0
        title: 'Job outcomes (completed vs failed)'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'traces\n| where severityLevel >= 3\n| where customDimensions has "job.id" or message has "Job" or message has "Scorer"\n| project timestamp, severityLevel, message, jobId = tostring(customDimensions["job.id"]), movie = tostring(customDimensions["movie.requested"]), operation_Id\n| order by timestamp desc\n| take 50'
        size: 0
        title: 'Recent errors and warnings'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
        visualization: 'table'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'requests\n| where name has "/api/jobs"\n| summarize calls = count(), avgMs = avg(duration), p95 = percentile(duration, 95), failures = countif(success == false) by name, resultCode\n| order by calls desc'
        size: 0
        title: 'HTTP /api/jobs latency'
        timeContext: { durationMs: 86400000 }
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.insights/components'
        visualization: 'table'
      }
    }
  ]
  styleSettings: {}
}

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: workbookId
  location: location
  tags: tags
  kind: 'shared'
  properties: {
    displayName: 'Movie Rating Agent — Gen AI'
    serializedData: string(workbookContent)
    sourceId: appInsights.id
    category: 'workbook'
    version: '1.0'
  }
}

@description('Workbook resource name (GUID).')
output workbookName string = workbook.name
