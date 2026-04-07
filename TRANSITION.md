# TRANSITION.md — moving Movie Rating Agent into BillDevPlayground

This runbook captures the **one-time migration** of the Movie Rating Agent
from its existing home in the **OpenEval** subscription to a fresh, standalone
deployment in the **BillDevPlayground** subscription, plus the configuration of
**movieratingagent.com** on Cloudflare DNS, plus the cleanup of the old
deployment.

It is intentionally exhaustive — every step that touches Azure, GitHub, or
Cloudflare is spelled out so the run can be paused and resumed safely.

---

## 0. Source / target inventory

| Aspect | Value |
| --- | --- |
| **Old subscription** | `OpenEval` — `cc73752c-0fe7-4a20-bad5-27505cada36c` |
| **Old resource group** | `rg-movie-rating-agent-dev` |
| **New subscription** | `BillDevPlayground` — `379168a0-b9fc-4fa0-a3cd-ce32ab20ee70` |
| **Tenant** (both subs) | `5c369887-a4a0-4a67-a8d6-a78e017216fc` |
| **New resource group** | `rg-movie-rating-agent-dev` (same name, different sub) |
| **Region** | `eastus2` |
| **Domain** | `movieratingagent.com` (apex + `www`), DNS hosted on Cloudflare |
| **Domain layout** | apex + www → SWA; API stays on the SWA `/api` linked-backend route |
| **GitHub repo** | `CrankingAI/movie-rating-agent` |

> Note: BillDevPlayground was recently renamed (from `OpenSesame`). The
> rename has propagated, but the subscription **ID** remains the source of
> truth in scripts and CI for safety against any future renames.

---

## 1. Pre-migration checklist

Run through this list before touching anything:

- [ ] You have **Owner** (or at least Contributor + User Access Administrator)
      on the BillDevPlayground subscription.
- [ ] You have **Owner** of the Cloudflare zone for `movieratingagent.com`.
- [ ] You have the GitHub `gh` CLI installed and authenticated as a maintainer
      of `CrankingAI/movie-rating-agent`.
- [ ] You have `az` CLI 2.60+ and Bicep 0.30+ installed.
- [ ] Docker Desktop runs locally (only needed for local Aspire dev — not
      migration itself).
- [ ] The latest `main` branch builds clean: `dotnet build MovieRatingAgent.slnx`.
- [ ] The repo's `scripts/deploy-config.sh` already points at the new
      subscription ID `379168a0-b9fc-4fa0-a3cd-ce32ab20ee70` (it does as of this
      runbook — verify with `grep AZURE_SUBSCRIPTION_ID scripts/deploy-config.sh`).
- [ ] Capture the current Foundry endpoint + key from the **old** deployment
      in case anything in `local.settings.json` still depends on it
      (`scripts/set-local-creds.sh` reads from the old sub by default — change
      `AI_NAME`/`RESOURCE_GROUP` exports temporarily if you need both).

---

## 2. Stand up the new deployment

### 2a. Authenticate to the new subscription

```bash
az login                                    # if not already
az account set --subscription 379168a0-b9fc-4fa0-a3cd-ce32ab20ee70
az account show --query name -o tsv         # should print: BillDevPlayground
```

### 2b. Warm up the new subscription

`BillDevPlayground` is a fresh subscription, so the providers need to be
registered and the model availability checked before the first Bicep deploy.
The warmup script handles all of that idempotently:

```bash
./scripts/warmup-new-sub.sh
```

It will:

1. Verify your `az` session can reach the target subscription.
2. Register `Microsoft.Web`, `Microsoft.Storage`, `Microsoft.CognitiveServices`,
   `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.Resources`.
3. Poll until each provider is in `Registered` state.
4. Confirm `gpt-4o` and `gpt-4o-mini` are available in the target region;
   warn (but don't fail) if `gpt-5.4` is not.

If any required model is missing, the script exits non-zero and prints
guidance on picking a different region or requesting quota.

### 2c. Federate GitHub Actions to the new subscription

`scripts/setup-oidc.sh` is idempotent and reads the target sub from
`scripts/deploy-config.sh`:

```bash
./scripts/setup-oidc.sh
```

This will:

1. Create / reuse the `sp-movie-rating-agent-github` Entra app + service principal.
2. Assign **Contributor** on `/subscriptions/379168a0-b9fc-4fa0-a3cd-ce32ab20ee70`.
3. Create federated credentials for `repo:CrankingAI/movie-rating-agent:ref:refs/heads/main`
   and `repo:CrankingAI/movie-rating-agent:pull_request`.
4. Push `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` as
   GitHub repository secrets.

> If the old deployment created an Entra app with the same display name, the
> script will reuse it — but the **role assignment** is now on the new sub.
> The OpenEval sub will still have an old role assignment hanging off the same
> SP; clean it up in §5.

### 2d. Deploy via GitHub Actions (canonical path)

Deploys are 100% driven by `.github/workflows/deploy.yml`. Trigger a full
deploy with:

```bash
gh workflow run deploy.yml -f target=all
```

The workflow has three independent jobs (`deploy-infra`, `deploy-functions`,
`deploy-swa`), each gated by a path filter, so a push that touches only
`swa/**` runs only the SWA job and a push to `src/**` runs only the
Functions job. Use the `target` input to scope a manual run:

```bash
gh workflow run deploy.yml -f target=infra      # Bicep only
gh workflow run deploy.yml -f target=functions  # ZIP-deploy the worker only
gh workflow run deploy.yml -f target=swa        # SWA content only
```

Tail the run with:

```bash
gh run watch $(gh run list --workflow=deploy.yml --limit 1 --json databaseId -q '.[0].databaseId')
```

Expected result: a new `rg-movie-rating-agent-dev` in BillDevPlayground
containing:

* `log-movie-rating-agent-dev` (Log Analytics)
* `appi-movie-rating-agent-dev` (Application Insights) + the
  **"Movie Rating Agent — Gen AI"** workbook
* `stmradev<token>` (Storage account with `jobs` blob container + `job-requests` queue)
* `ai-movie-rating-agent-dev-<token>` (Azure AI Services + 2 model deployments:
  gpt-4o, gpt-4o-mini — gpt-5.4 is gated behind `deployGpt54=true` and
  preview-model quota)
* `plan-movie-rating-agent-dev` (B1 Linux App Service Plan)
* `func-movie-rating-agent-dev-<token>` (Function App)
* `swa-movie-rating-agent-dev` (Standard SWA, linked-backend → Function App)

`<token>` is a 6-character deterministic suffix derived from the
subscription ID (`take(replace(subscription().subscriptionId, '-', ''), 6)`).
For BillDevPlayground (`379168a0-...`) it resolves to `379168`. The token
makes globally-unique names (storage account, AI Services subdomain,
Function App hostname) collision-free across subscriptions.

> **Local fallback:** `./scripts/deploy.sh --infra-only` still exists as a
> break-glass path for emergencies, but the workflow is the canonical path.
>
> **gpt-5.4 quota gotcha:** the `gpt-5.4` model is preview-gated and most
> subscriptions don't have access. The Bicep ships with `deployGpt54=false`
> by default. To enable it, request quota in the Azure portal, then edit
> `infra/main.bicepparam` to set `deployGpt54 = true` and
> `defaultModelId = 'gpt-5.4'`, and re-run the workflow.

### 2f. Smoke test

```bash
./scripts/test-cloud.sh "Heat"
```

You should see a status progression of `Queued → Running → Completed` and a
JSON result with a `score`, `subScores`, and `infoUrl`.

Also confirm telemetry:

```bash
./scripts/view-otel.sh --genai --timespan PT15M
```

You should see `gen_ai` dependency spans for each scorer (Popularity,
ArtisticValue, Iconicness) and the title resolver.

---

## 3. Configure movieratingagent.com on Cloudflare

The chosen domain layout is **apex + www → SWA**, with the API reachable on
the SWA `/api` proxy (no separate `api.` subdomain). This needs a two-pass
deploy because Cloudflare records have to exist *before* the
`cname-delegation` validation can succeed.

### 3a. Read the SWA's default hostname

```bash
SWA_HOST=$(az staticwebapp show \
  --name swa-movie-rating-agent-dev \
  --resource-group rg-movie-rating-agent-dev \
  --query defaultHostname -o tsv)
echo "$SWA_HOST"
# example: nice-pebble-0a1b2c3d4.5.azurestaticapps.net
```

### 3b. Create the DNS records in Cloudflare

In the Cloudflare dashboard for the `movieratingagent.com` zone, add:

| # | Type | Name | Target | Proxy status |
| - | --- | --- | --- | --- |
| 1 | `CNAME` | `@` (apex) | `<value of $SWA_HOST>` | **DNS only** (gray cloud) |
| 2 | `CNAME` | `www` | `<value of $SWA_HOST>` | **DNS only** (gray cloud) |

> **Cloudflare CNAME flattening** lets you put a CNAME on the apex; that's
> required because Azure SWA cannot be reached via an A record.
>
> **Proxy must be DNS-only** during validation. After the SWA reports
> `Ready`, you may flip the proxy back on if you want Cloudflare's CDN —
> but note that proxied traffic re-encrypts at Cloudflare and the SWA's
> own TLS chain becomes invisible to clients. For a community example, leaving
> proxying off is the simplest path.

### 3c. Run the domain bring-up script

```bash
./scripts/setup-custom-domain.sh
```

The script will:

1. Echo back the records you just created (sanity check).
2. Trigger the apex `dns-txt-token` validation on the SWA.
3. Print a **TXT** record value that you must add as record #3:

   | # | Type | Name | Value |
   | - | --- | --- | --- |
   | 3 | `TXT` | `@` | `<token from script>` |

4. Wait for Azure to validate the TXT (5–10 minutes typical).
5. Bind `www.movieratingagent.com` via `cname-delegation` (succeeds
   immediately because record #2 is already in place).

### 3d. Switch infra deploys to the with-domain parameter file

After the script reports both hostnames `Ready`, edit any subsequent infra
runs to use the parameter file that declares the custom domain so future
re-deploys keep the binding declarative:

```bash
./scripts/deploy.sh --infra-only --with-domain
```

In GitHub Actions, dispatch the `Deploy` workflow with `with_domain = true`.
Don't make this the *default* until you've cut over to the custom domain
and verified the binding is stable in production.

### 3e. Verify

```bash
curl -sI https://movieratingagent.com/         | head -5
curl -sI https://www.movieratingagent.com/     | head -5
curl -s  https://www.movieratingagent.com/api/readyz | python3 -m json.tool
```

Expected: HTTP 200 on the apex/www, plus `/api/readyz` returning the
deployed version + commit.

---

## 4. Cut over from the old deployment

At this point the new deployment is fully functional but no public traffic
points at it yet (clients still hit
`https://swa-movie-rating-agent-dev.<old-suffix>.azurestaticapps.net` if any
were hard-coded).

### 4a. Update any external references

Grep for the old SWA hostname and any hard-coded `func-movie-rating-agent-dev`
URLs that reference the **old** subscription's resources:

```bash
git grep -nI 'azurestaticapps\|azurewebsites' -- ':!*.lock' ':!*/bin/*' ':!*/obj/*'
```

In particular check:

* `swa/index.html` (the README + footer reference)
* `README.md` and `swa/index.html` already reference `www.movieratingagent.com`,
  so once DNS is live there's nothing to change.
* `local.settings.json` (gitignored — your local Foundry endpoint will need
  refreshing via `./scripts/set-local-creds.sh` after §5).

### 4b. Final pre-cleanup smoke test

```bash
curl -s https://www.movieratingagent.com/api/readyz
curl -s https://www.movieratingagent.com/api/livez
./scripts/test-cloud.sh "The Godfather" www.movieratingagent.com
./scripts/view-otel.sh --genai --timespan PT15M
```

If any of these fail, **stop** and resolve before continuing to §5. Once the
old RG is deleted there is no rollback target other than re-running the
Bicep against the same names in the old sub.

---

## 5. Decommission the old OpenEval deployment

> ⚠️ **Destructive.** Read this whole section before running any commands.
> Resource-group deletion is irreversible and takes ~10 minutes to complete.

### 5a. Capture anything you want to keep

Optional but recommended: snapshot the existing job blobs and the App
Insights export so you can compare before/after.

```bash
# Export the existing jobs blob container metadata
az storage blob list \
  --account-name stmradev \
  --container-name jobs \
  --subscription cc73752c-0fe7-4a20-bad5-27505cada36c \
  --auth-mode login \
  --output table > /tmp/old-jobs-snapshot.txt
```

### 5b. Delete the old resource group

```bash
az group delete \
  --name rg-movie-rating-agent-dev \
  --subscription cc73752c-0fe7-4a20-bad5-27505cada36c \
  --yes --no-wait
```

Track progress with:

```bash
az group show \
  --name rg-movie-rating-agent-dev \
  --subscription cc73752c-0fe7-4a20-bad5-27505cada36c \
  --query properties.provisioningState -o tsv 2>&1
# Will say "Deleting" then eventually error 'ResourceGroupNotFound' once gone.
```

### 5c. Soft-deleted resources to purge

Three resource types use Azure's "soft delete" model and remain reserved
even after the RG is gone, blocking re-creation in any sub for 7–90 days
unless you purge them:

1. **Cognitive Services account** — `ai-movie-rating-agent-dev`. Purge via:

   ```bash
   az cognitiveservices account purge \
     --location eastus2 \
     --resource-group rg-movie-rating-agent-dev \
     --name ai-movie-rating-agent-dev \
     --subscription cc73752c-0fe7-4a20-bad5-27505cada36c
   ```

2. **Application Insights component** — `appi-movie-rating-agent-dev`.
   With Workspace-based AI (which is what we use) this is automatically
   freed when the LA workspace is dropped — no manual purge needed.

3. **Log Analytics workspace** — `log-movie-rating-agent-dev`. Purge via:

   ```bash
   az monitor log-analytics workspace delete \
     --workspace-name log-movie-rating-agent-dev \
     --resource-group rg-movie-rating-agent-dev \
     --subscription cc73572c-0fe7-4a20-bad5-27505cada36c \
     --force true --yes
   ```

   (Already deleted as part of the RG; this command is the explicit "purge
   the soft-deleted record" if it lingers.)

### 5d. Remove the old role assignment from the SP

The Entra app `sp-movie-rating-agent-github` had a Contributor role on the
**old** sub. After §2c it has Contributor on the new sub *as well*. Drop
the old assignment so the SP only has access to one place:

```bash
SP_OBJECT_ID=$(az ad sp list \
  --display-name sp-movie-rating-agent-github \
  --query "[0].id" -o tsv)
az role assignment delete \
  --assignee-object-id "$SP_OBJECT_ID" \
  --assignee-principal-type ServicePrincipal \
  --scope "/subscriptions/cc73752c-0fe7-4a20-bad5-27505cada36c"
```

### 5e. Local dev hygiene

* `src/MovieRatingAgent.Functions/local.settings.json` (gitignored) still
  contains the **old** Foundry endpoint and key plus the **old** App
  Insights connection string. Refresh:

  ```bash
  ./scripts/set-local-creds.sh
  ```

  This re-fetches the endpoint+key from the **new** subscription and
  pushes them into the Aspire AppHost user-secrets store.

* `./otel-export/*.jsonl` files are left over from prior local runs. Safe
  to delete (`rm -f otel-export/*.jsonl`) — they will be regenerated on the
  next `./scripts/run-local.sh`.

---

## 6. Post-migration verification (sign-off list)

- [ ] `https://movieratingagent.com/` returns 200
- [ ] `https://www.movieratingagent.com/` returns 200
- [ ] `https://www.movieratingagent.com/api/readyz` returns the latest
      `version` + commit
- [ ] `./scripts/test-cloud.sh "Heat" www.movieratingagent.com` completes
- [ ] App Insights → Workbooks → **Movie Rating Agent — Gen AI** loads
      and shows non-zero data
- [ ] Local Aspire run produces gen_ai spans in
      https://localhost:15888 and `./otel-export/traces.jsonl`
- [ ] GitHub Actions `Deploy` workflow run on `main` succeeds end-to-end
- [ ] Old `rg-movie-rating-agent-dev` in OpenEval no longer exists
- [ ] Old SP role assignment on OpenEval is removed
- [ ] Cloudflare zone has only the three records described in §3b/§3c

---

## 7. Rollback playbook

If something fails before §5, rollback is trivial: the old deployment is
untouched in OpenEval. Just point traffic back at the old SWA hostname
(by removing the Cloudflare CNAMEs) and re-federate the GitHub Actions
secrets to the old subscription. Override the deploy-config sub when
running setup-oidc:

```bash
AZURE_SUBSCRIPTION_ID=cc73752c-0fe7-4a20-bad5-27505cada36c \
  ./scripts/setup-oidc.sh
```

If something fails *after* §5, rollback means re-running this whole
TRANSITION.md against OpenEval (which is essentially a new deployment in
the old sub). Plan accordingly.

---

## 8. What changed in the repo for this transition

For reviewers, the diffs that go with this runbook:

* `infra/main.bicep` — adds `customDomain` / `includeWwwSubdomain` /
  `tags` parameters and a `workbook` module.
* `infra/staticWebApp.bicep` — apex (`dns-txt-token`) and www
  (`cname-delegation`) custom domain resources gated on `customDomain`.
* `infra/main.bicepparam` — first-pass parameters (no domain).
* `infra/main.with-domain.bicepparam` — second-pass parameters that bind
  the domain.
* `infra/workbook.bicep` — pre-canned Azure Monitor workbook for gen_ai.
* `infra/functionApp.bicep` — adds `OTEL_SERVICE_NAME` /
  `OTEL_RESOURCE_ATTRIBUTES` to App Settings; passes `tags` through.
* `infra/foundry.bicep`, `infra/storage.bicep`, `infra/monitoring.bicep` —
  accept `tags` parameter.
* `scripts/deploy-config.sh` — sets `AZURE_SUBSCRIPTION_ID` /
  `AZURE_TENANT_ID` to BillDevPlayground; introduces
  `BICEP_PARAMS_WITH_DOMAIN` and `CUSTOM_DOMAIN`.
* `scripts/deploy.sh` — adds `--with-domain`, sub guardrail, uses the
  bicepparam files instead of CLI `--parameters`.
* `scripts/setup-oidc.sh` — idempotent, reads target sub from
  `deploy-config.sh`, also creates a `pull_request` federated credential.
* `scripts/setup-custom-domain.sh` — new helper that walks the apex/www
  validation handshake.
* `scripts/run-local.sh` — clearer pre-flight banner, surfaces the
  Aspire dashboard URL and the `GENAI_CAPTURE_MESSAGE_CONTENT` toggle.
* `aspire/MovieRatingAgent.AppHost/AppHost.cs` — passes
  `OTEL_SERVICE_NAME` / `OTEL_RESOURCE_ATTRIBUTES` /
  `GENAI_CAPTURE_MESSAGE_CONTENT` env to the Functions worker.
* `src/MovieRatingAgent.Agent/ServiceCollectionExtensions.cs` — honors
  the gen_ai message-content capture toggle.
* `.github/workflows/deploy.yml` — split into infra / functions / swa
  jobs with `paths:` filters and a `workflow_dispatch` `target` input.
