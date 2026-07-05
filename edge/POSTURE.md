# Edge posture record — movieratingagent.com

Applied 2026-07-05 (Claude session, AGENTS.md Seed Kit v0.4.1 field run).
This file is the durable record of state that lives in Cloudflare/Azure, not
in code. Update it when the posture changes.

## Cloudflare zone (Free plan)

- **Zone:** `movieratingagent.com`, zone id `e2905cd045294b0d6102b35ca6c5d384`,
  account id `4286648997bdbc7afa1ca29003992e7a`
- **DNS:** apex + `www` CNAME → `wonderful-grass-043c6bb0f.6.azurestaticapps.net`
  (Azure SWA `swa-movie-rating-agent-dev`), both **proxied** (orange cloud)
- **Settings:** SSL **Full (strict)** · min TLS **1.2** · Always Use HTTPS **on**
  (previously: full / 1.0 / off)
- **Rulesets** (created by the kit's `cf-zone-init.sh`):
  - cache **bypass** on the watched paths
  - `www` → apex **301** redirect
  - transform rule stamping `x-kit-verified-bot` / `x-kit-verified-bot-category`
    from `cf.client.bot` / `cf.verified_bot_category`, unconditionally on every
    request (anti-spoofing: client-supplied values are always overwritten).
    This is the Worker's verified-bot source — `request.cf.botManagement` is
    Enterprise-only and absent on this plan (verified live).
- **Worker:** `movieratingagent-edge` on the six watched-path routes, all with
  `request_limit_fail_open: true` (free-tier daily cap ⇒ origin serves,
  logging gap only). Re-apply after any route-recreating deploy:

  ```bash
  # per route id:
  curl -X PUT "https://api.cloudflare.com/client/v4/zones/<ZONE>/workers/routes/<ID>" \
    -H "Authorization: Bearer $CF_API_TOKEN" -H "Content-Type: application/json" \
    --data '{"pattern":"<pattern>","script":"movieratingagent-edge","request_limit_fail_open":true}'
  ```

- **Analytics Engine:** dataset `edge_hits`, binding `EDGE_HITS` (account-level
  AE was enabled once in the dash — required before the first AE-bound deploy;
  API error 10089 otherwise).

## Azure log plumbing (10-day forensic sink)

Subscription **BillDev** `379168a0-b9fc-4fa0-a3cd-ce32ab20ee70`, resource group
`rg-movie-rating-agent-dev` (deployed from the kit's
`seed/infra/modules/edge-logs.bicep`, unmodified):

- **Table:** `EdgeHits_CL` in workspace `log-movie-rating-agent-dev`
  (customerId `67187cbd-2c5a-4cb2-a78d-8f716993f8f9`), retention **10 days**
  (`retentionInDays` + `totalRetentionInDays` — the EFF-DNT window; raw IP/UA
  live here and nowhere else)
- **DCE:** `dce-movie-rating-agent-dev` — ingestion endpoint
  `https://dce-movie-rating-agent-dev-3nr7.eastus2-1.ingest.monitor.azure.com`
- **DCR:** `dcr-edge-movie-rating-agent-dev` — immutable id
  `dcr-8a4bb8932b5c4549821ac1ce1a51cf0a`, stream `Custom-EdgeHits_CL`
- **Identity:** Entra app `movie-rating-agent-edge-logs`, client id
  `412527f2-be85-4dfd-9590-4d604c2c235b`, tenant
  `5c369887-a4a0-4a67-a8d6-a78e017216fc`, client secret expires **2026-07-05+1y**
  (rotate: `az ad app credential reset` → `wrangler secret put AZURE_CLIENT_SECRET`).
  Sole role: **Monitoring Metrics Publisher** on the DCR.

  > ⚠️ Tenant trap (hit live): `az ad app create` runs in the tenant of the
  > CLI's *current* subscription context — this app must live in the tenant of
  > the subscription holding the DCR (BillDev, not the default). A role
  > assignment with explicit `principalType` will silently "succeed" for a
  > ghost principal from another tenant; the symptom is `AADSTS700016` at
  > token time.

## Queries

```bash
# DNT-safe trends (Analytics Engine, ~3 months)
edge-hits            # all traffic, last 7 days
edge-hits -v 30      # verified bots, last 30 days

# Full-fidelity forensics (Log Analytics, 10 days)
az monitor log-analytics query \
  --subscription 379168a0-b9fc-4fa0-a3cd-ce32ab20ee70 \
  -w 67187cbd-2c5a-4cb2-a78d-8f716993f8f9 \
  --analytics-query "EdgeHits_CL | where Verified == false
    | project TimeGenerated, Path, UserAgent, Ip, Country | order by TimeGenerated desc"
```

## First light

- First verified crawler logged: **OAI-SearchBot** reading `/robots.txt`,
  2026-07-05 09:04 UTC (category "Search Engine Crawler"); Googlebot and
  facebookexternalhit ("Page Preview") followed the same day.
- Cost shape: everything above is $0 at this traffic level. The only shared
  pools are account-wide Workers requests (100k/day free, fail-open on excess)
  and AE writes (100k/day). The escape valve is Workers Paid, $5/mo for the
  whole account. Azure: `EdgeHits_CL` ingestion is pennies/month at
  watched-path volume.
