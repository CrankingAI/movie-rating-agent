# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A movie rating AI agent that rates movies for "greatness" on a 0-100 scale using a multi-dimensional evaluation framework (Popularity 30%, Artistic Value 40%, Iconicness 30%). The agent resolves movie titles to canonical names with release year, provides an info URL (official site > Wikipedia > IMDb > Google), and returns structured pros/cons with conflict detection.

Includes a comprehensive batch evaluation framework for comparing LLM performance across models, prompts, and temperatures with rigorous statistical analysis.

## Tech Stack

- **.NET 10**, C#, isolated worker model
- **Microsoft.Extensions.AI Agent Framework** — `WorkflowBuilder` with fan-out/fan-in executors (`Microsoft.Agents.AI.Workflows`)
- **Microsoft Foundry** back-end for LLMs — deployed models: **gpt-4o**, **gpt-4o-mini** (always); **gpt-5.4** and **gpt-5.5** opt-in via Bicep flags (quota-gated)
- **Microsoft.Extensions.AI.Evaluation** — `CoherenceEvaluator`, `RelevanceEvaluator`, `GroundednessEvaluator`
- **Azure Functions** (Linux, App Service plan with AlwaysOn, .NET 10 isolated worker)
- **Azure Static Web Apps** — Movie rating UI frontend with Rate + Learn tabs
- **Pico CSS** v2 (classless, CDN) — semantic HTML styling with dark theme and cinema amber accent
- **Azure Storage** — Blob Storage for job persistence (`jobs/{jobId}/`), Storage Queue for job dispatch
- **Bicep** for all infrastructure-as-code
- **OpenTelemetry (OTel)** + **.NET Aspire** for local dev observability
- **Azure Monitor** + **Application Insights** + **Log Analytics** for deployed monitoring
- **GitHub Actions** with OIDC auth for CI/CD

## Solution Structure

```bash
MovieRatingAgent.slnx
src/
  MovieRatingAgent.Functions/        # Azure Functions (SubmitJob, GetJob, RunJob, Readyz, Livez)
  MovieRatingAgent.Agent/            # WorkflowBuilder pipeline + fan-out/fan-in executors
  MovieRatingAgent.Core/             # Shared models, blob/queue services, AgentVersion, telemetry tags
tests/
  MovieRatingAgent.Tests/            # xUnit unit tests (16 tests)
  MovieRatingAgent.Eval/             # xUnit eval tests (M.E.AI.Evaluation, range, stability)
tools/
  MovieRatingAgent.BatchEval/        # Console app — batch eval matrix runner + statistical analysis
  GenerateWorkflowDiagram/           # Console app — calls ToMermaidString() on the workflow, outputs SVG
aspire/
  MovieRatingAgent.AppHost/          # Aspire host (local dev: Azurite, OTel Collector, Functions)
  MovieRatingAgent.ServiceDefaults/  # OTel + Azure Monitor + file export + health checks + resilience
swa/
  index.html                         # SWA frontend (Rate tab + Learn tab)
  agent-workflow.svg                 # Rendered from agent-workflow.mmd via mermaid-cli
  job-lifecycle.svg                  # Rendered from job-lifecycle.mmd via mermaid-cli
  staticwebapp.config.json           # SWA routing config
infra/
  main.bicep                         # Orchestrator (subscription scope)
  foundry.bicep                      # Azure AI Services + 3 model deployments
  storage.bicep                      # Storage Account + blob container + queue
  functionApp.bicep                  # Function App (CORS enabled)
  staticWebApp.bicep                 # Static Web App
  monitoring.bicep                   # Log Analytics + Application Insights
scripts/                             # All CLI-driven, no portal visits required
eval-results/                        # Batch eval output (36 combo files + analysis.json + quality_eval.json)
```

## Architecture

### Async Job Pattern (HTTP 202)

1. **POST /api/jobs** → 202 Accepted with `{ jobId }`. Writes `request.json` + `meta.json` (Queued) to blob, enqueues jobId.
2. **Queue trigger** → RunJob reads request, runs the agent, writes `response.json` + updates `meta.json` (Completed/Failed).
3. **GET /api/jobs/{jobId}** → Returns meta + result (if complete). Client polls every 2s.

### Agent Workflow (fan-out / fan-in)

```plaintext
Title Resolution (LLM: canonical title + year + info URL)
  ↓
Start ──fanout──┬── PopularityScorer (LLM, 0-100 + pros/cons)
                ├── ArtisticValueScorer (LLM, 0-100 + pros/cons)
                └── IconicnessScorer (LLM, 0-100 + pros/cons)
                    ↓ fan-in barrier
                ResultCollector
                    ↓ conditional (all 3 results)
                ProsConsRollup (LLM: merge, deduplicate, detect conflicts)
                    ↓
                WeightedScoreRollup (pure math: 30/40/30 weighted average)
```

Diagrams generated from code via `WorkflowVisualizer.ToMermaidString()`:

- `agent-workflow.mmd` → `swa/agent-workflow.svg`
- `job-lifecycle.mmd` → `swa/job-lifecycle.svg`

Regenerate: `dotnet run --project tools/GenerateWorkflowDiagram` then `npx @mermaid-js/mermaid-cli --input agent-workflow.mmd --output swa/agent-workflow.svg --backgroundColor transparent`

### Blob Layout

```yaml
jobs/{jobId}/request.json    # { "topic": "Heat" }
jobs/{jobId}/response.json   # { "ratedMovie": "Heat", "releaseYear": 1995, "score": 90, "infoUrl": "...", ... }
jobs/{jobId}/meta.json       # { "status": "Completed", "agentVersion": "0.1.0", ... }
```

## Versioning

- **Single source of truth**: `VERSION` file at repo root (e.g., `0.1.0`)
- **`Directory.Build.props`** reads `VERSION`, sets all assembly version properties
- **`Directory.Build.targets`** embeds git commit SHA as `AssemblyMetadataAttribute("CommitHash", "...")`
- **`AgentVersion.Current`** → `"0.1.0"`, **`AgentVersion.CommitHash`** → full 40-char SHA
- **`/api/readyz`** → `{ "status": "ready", "version": "0.1.0", "commit": "ed64305..." }`
- **`/api/livez`** → `{ "status": "alive", "quote": "It's alive! It's alive!", "attribution": "Henry Frankenstein, Frankenstein (1931)" }`
- **SWA footer** displays `v0.1.0 (ed64305)` fetched from `/readyz`

### Version Bumping

```bash
./scripts/bump-version.sh              # auto-detect from conventional commits since last tag
./scripts/bump-version.sh major        # force major bump
./scripts/bump-version.sh minor        # force minor bump
./scripts/bump-version.sh patch        # force patch bump
./scripts/bump-version.sh --dry-run    # preview without changes
```

Auto-detection scans `git log` since last `v*` tag:

- `feat!:` or `BREAKING CHANGE` → major
- `feat:` → minor
- everything else → patch

Creates a commit + tag `vX.Y.Z`.

## Observability

### OTel Instrumentation

All spans follow [OpenTelemetry Gen AI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/):

- `gen_ai.operation.name` = `"chat"` or `"invoke_agent"`
- `gen_ai.agent.name` = scorer/executor ID
- `gen_ai.request.model` = deployment name
- `gen_ai.provider.name` = `"azure.ai.inference"`

Custom tags: `job.id`, `job.status`, `movie.requested`, `movie.rated`, `movie.score`

### Export Targets

| Context | Exporter | Activation |
|---------|----------|------------|
| Local (Aspire) | OTLP → Aspire dashboard | `OTEL_EXPORTER_OTLP_ENDPOINT` |
| Local (file) | OTLP → OTel Collector → `./otel-export/*.jsonl` | `OTEL_FILE_EXPORTER_ENDPOINT` |
| Deployed | Azure Monitor → Application Insights | `APPLICATIONINSIGHTS_CONNECTION_STRING` |

### Viewing Telemetry

```yaml
./scripts/view-otel.sh                  # Azure Monitor, last 1 hour
./scripts/view-otel.sh --local          # Aspire local file export
./scripts/view-otel.sh --genai          # Gen AI / LLM focused queries
./scripts/view-otel.sh --timespan PT4H  # Custom time window
```

## Batch Evaluation

### What It Tests

The `tools/MovieRatingAgent.BatchEval` console app runs a full combinatorial matrix:

- **3 models**: gpt-5.4, gpt-4o, gpt-4o-mini
- **2 prompt variants**: detailed (verbose with examples) vs concise (stripped-down)
- **2 temperatures**: 0.0 (deterministic) vs 0.7 (creative)
- **3 movies**: The Godfather, Santa Claus Conquers the Martians, Citizen Kane
- **100 runs per combination** = 3,600 total agent invocations

### Statistical Analysis

Per-combination: mean, median, std dev, variance, 95% CI, coefficient of variation, skewness, kurtosis, IQR, range.

Pairwise comparisons: **Mann-Whitney U test** (non-parametric, correct for non-normal LLM score distributions), **Cohen's d** effect size (negligible/small/medium/large), p-value significance at 0.05.

### M.E.AI.Evaluation Quality Metrics

Uses three evaluators from `Microsoft.Extensions.AI.Evaluation.Quality`:

- **CoherenceEvaluator** — is the output logically consistent?
- **RelevanceEvaluator** — does the output address the prompt?
- **GroundednessEvaluator** — is the output grounded in provided context? (correctly returns 0 for subjective opinion tasks)

### Running

```bash
export FOUNDRY_ENDPOINT="..." FOUNDRY_API_KEY="..."
dotnet run --project tools/MovieRatingAgent.BatchEval           # full run (~5 hours)
dotnet run --project tools/MovieRatingAgent.BatchEval -- --analyze-only  # re-analyze existing results
./scripts/run-batch-eval.sh                                     # fetches creds from Azure automatically
```

Results: `eval-results/` (36 per-combination JSON files + `analysis.json` + `quality_eval.json` + `FINDINGS.md`)

## SWA Frontend

Built with **Pico CSS v2** (classless variant, loaded from jsDelivr CDN). Uses semantic HTML (`<header>`, `<main>`, `<footer>`, `<article>`, `<section>`, `<nav>`, `<details>`, `<fieldset role="group">`, `<kbd>`, `<blockquote>`) so Pico styles everything automatically. Dark theme forced via `data-theme="dark"` with a cinema amber accent color (`--pico-primary: #f0a030`). Custom CSS is limited to app-specific components (score badge, sub-scores grid, pros/cons layout, spinner).

- **Rate tab**: Movie name input → async job submission → poll → display score badge, sub-scores, reasoning, pros/cons, info link
- **Learn tab**: How it works, agent workflow diagram (SVG), job lifecycle sequence diagram (SVG), scoring dimensions table, pipeline steps, tech stack badges
- **Footer**: domain link + "Powered by Agent Framework · Microsoft Foundry · Microsoft Azure" + repo link + version from `/readyz`
- **Movie display**: Shows "Title (Year)" with a "More about this film" link (priority: official site > Wikipedia > IMDb > Google search)

### Deploying the SWA

```bash
DEPLOY_TOKEN=$(az staticwebapp secrets list --name swa-movie-rating-agent-dev --resource-group rg-movie-rating-agent-dev --query "properties.apiKey" -o tsv)
npx @azure/static-web-apps-cli deploy ./swa --deployment-token "$DEPLOY_TOKEN" --env default
```

## Health Endpoints

- **GET /api/readyz** — Returns `{ status, version, commit }`. Use for deployment verification and SWA version display.
- **GET /api/livez** — Returns `{ status, quote, attribution }`. Liveness check.

## Azure Resources (dev environment)

All resources in `rg-movie-rating-agent-dev` (eastus2) in subscription
`BillDev` (`379168a0-b9fc-4fa0-a3cd-ce32ab20ee70`):

- **AI Services**: `ai-movie-rating-agent-dev-<token>` — 2 GA deployments
  (gpt-4o, gpt-4o-mini); gpt-5.4 and gpt-5.5 are opt-in via Bicep
  `deployGpt54=true` / `deployGpt55=true` (each requires sub quota)
- **Storage**: `stmradev<token>` — blob container `jobs`, queue `job-requests`
- **Function App**: `func-movie-rating-agent-dev-<token>` — .NET 10 isolated, AlwaysOn
- **Static Web App**: `swa-movie-rating-agent-dev` — Standard tier, linked-backend → Function App
- **App Insights**: `appi-movie-rating-agent-dev` + "Movie Rating Agent — Gen AI" workbook
- **Log Analytics**: `log-movie-rating-agent-dev`

`<token>` is a 6-char deterministic suffix derived from the subscription ID
(`take(replace(subscription().subscriptionId, '-', ''), 6)`); for
BillDev it resolves to `379168`. Bash equivalent:
`echo "$AZURE_SUBSCRIPTION_ID" | tr -d - | cut -c1-6`. Re-deploys are
idempotent because the same sub always produces the same token, and two
different subs can deploy the same Bicep without collision.

## Deploys

The canonical deploy path is the GitHub Actions workflow at
`.github/workflows/deploy.yml`. Push to `main` (path filters route SWA-only
or Functions-only changes to the right job) or trigger manually:

```bash
gh workflow run deploy.yml -f target=all
gh workflow run deploy.yml -f target=infra
gh workflow run deploy.yml -f target=functions
gh workflow run deploy.yml -f target=swa
gh workflow run deploy.yml -f target=infra -f with_domain=true
```

`scripts/deploy.sh` is preserved as a break-glass / local-only fallback.
See `TRANSITION.md` for the full BillDev bring-up + DNS cutover
runbook.

## Scripts

| Script | Purpose |
|--------|---------|
| `warmup-new-sub.sh` | Prepare a fresh sub: register providers, verify model availability |
| `setup-oidc.sh` | Create Entra app reg + OIDC federated creds + GitHub secrets |
| `setup-custom-domain.sh` | Walk the apex/www validation handshake on Cloudflare |
| `deploy.sh` | EMERGENCY-ONLY local deploy. Prefer GitHub Actions. |
| `run-local.sh` | Start Aspire AppHost for local dev |
| `run-eval.sh` | Run xUnit eval tests (range, stability, quality) |
| `run-batch-eval.sh` | Run batch eval matrix (fetches creds from Azure) |
| `test-local.sh` | Submit job to local Functions, poll for result |
| `test-cloud.sh` | Submit job to deployed SWA, poll for result |
| `set-local-creds.sh` | Fetch Foundry creds from Azure, set as Aspire user-secrets |
| `start-docker.sh` | Ensure Docker Desktop is running |
| `view-otel.sh` | View OTel telemetry (Azure Monitor or local Aspire export) |
| `bump-version.sh` | Bump version (auto from conventional commits or manual) |

## Commit Messages

Use Conventional Commits format: `<type>: <description>`

- `feat:` — new functionality
- `fix:` — bug fixes, refactors, performance improvements
- `chore:` — everything else (deps, CI, docs)
- Append `!` for breaking changes (e.g., `feat!:`)
- For multiple changes, use the most impactful type

## Key Design Decisions

- **HTTP 202 async pattern** — Jobs are decoupled from the HTTP request via a Storage Queue. The agent runs on a queue trigger, not inline with the POST. This prevents HTTP timeouts on long-running LLM calls.
- **Fan-out/fan-in workflow** — Three scorers run in parallel via `WorkflowBuilder.AddFanOutEdge` + `AddFanInBarrierEdge`, reducing latency vs sequential execution.
- **Structured output** — All LLM calls use `GetResponseAsync<T>()` (JSON mode) for typed responses. Fallback logic handles parse failures gracefully.
- **Direct scorer calls for batch eval** — The batch eval bypasses the `WorkflowBuilder` framework and calls scorers directly with `Task.WhenAll` for speed and reliability at scale.
- **Prompt variants** — "detailed" prompts include examples and verbose guidance; "concise" prompts are stripped to essentials. Detailed prompts produce better score discrimination (statistically significant, large effect sizes).
- **Temperature 0.0** — Produces dramatically more stable scores. gpt-4o at t=0 is perfectly deterministic for clear-cut movies.
- **Version in `VERSION` file** — Single source of truth, read by MSBuild. Commit hash embedded separately as `AssemblyMetadataAttribute`. No SDK magic concatenation.
- **Info URL priority** — Official movie website > Wikipedia > IMDb > Google search. Resolved by the LLM during title resolution.
- **Mermaid diagrams from code** — `agent-workflow.mmd` generated by `WorkflowVisualizer.ToMermaidString()` on the actual workflow graph, not hand-written. Rendered to SVG by `@mermaid-js/mermaid-cli`.
- **Pico CSS classless** — The SWA uses semantic HTML styled by Pico CSS v2 (CDN, classless variant) instead of hand-written CSS. Dark theme is forced via `data-theme="dark"`. A cinema amber accent (`#f0a030`) overrides Pico's default blue via `--pico-primary-*` custom properties. Only app-specific components (score badge, sub-scores grid, pros/cons) need custom CSS.
