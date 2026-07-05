# edge/ — Cloudflare edge Worker for movieratingagent.com

The proxy-tier Worker deployed on this site's Cloudflare zone. It logs every
hit to the **watched paths** (`/robots.txt`, `/sitemap.xml`, `/llms.txt`,
`/llms-full.txt`, `/security.txt`, `/.well-known/*`) with verified-bot
classification, fail-open (the origin response always wins). Vendored from the
AGENTS.md Seed Kit (`DevPartners/meta-agent-md` v0.4.1, `seed/apps/edge/`);
conventions and query recipes live in that kit's `seed/docs/edge.md`.

This directory is the source of truth for the Worker **as deployed** — see
[POSTURE.md](POSTURE.md) for everything else that was applied to the zone and
the Azure log plumbing.

## Sinks (DNT split — enforced in `src/record.ts`)

| Sink | Retention | Gets |
|---|---|---|
| Log Analytics `EdgeHits_CL` (workspace `log-movie-rating-agent-dev`) | **10 days** | Everything incl. raw IP + UA |
| Analytics Engine `edge_hits` | ~3 months | DNT-safe fields; UA only for verified bots; never IP |
| PostHog | — | disabled (`POSTHOG_ENABLED=false`) |

Quick look from any terminal on this machine: `edge-hits` (or `edge-hits -v`
for verified bots only) — helper at `~/bin/edge-hits`.

## Deploy

```bash
cd edge
export CLOUDFLARE_ACCOUNT_ID=4286648997bdbc7afa1ca29003992e7a
export CLOUDFLARE_API_TOKEN=<token: Workers Scripts Write + Workers Routes Write>
npx wrangler@latest deploy
```

Secrets (already set on the deployed Worker; re-set with
`npx wrangler secret put <NAME>` if rotating): `AZURE_DCE_ENDPOINT`,
`AZURE_DCR_ID`, `AZURE_STREAM`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`,
`AZURE_CLIENT_SECRET`. Values/IDs: [POSTURE.md](POSTURE.md).

After any deploy that recreates routes, re-apply the **fail-open** flag on the
routes (`request_limit_fail_open: true` — API/dash only, not expressible in
`wrangler.jsonc`; see POSTURE.md) so the Workers free-tier daily cap can never
take the watched paths down.

## Test

```bash
cd edge && npm install && npx tsc --noEmit && npx vitest run
```
