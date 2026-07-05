// Edge logging Worker. Fail-open by construction: the origin response is
// fetched first and returned no matter what; every sink call is fire-and-
// forget inside ctx.waitUntil(...).catch(...). See docs/edge.md.

import type { EdgeCf } from "./record";
import {
  buildRecord,
  isWatched,
  resolveBot,
  toAnalyticsEngine,
  toLogAnalytics,
  toPostHog,
} from "./record";

export interface Env {
  EDGE_HITS: AnalyticsEngineDataset;
  AZURE_ENABLED: string;
  POSTHOG_ENABLED: string;
  POSTHOG_HOST: string;
  AZURE_DCE_ENDPOINT?: string;
  AZURE_DCR_ID?: string;
  AZURE_STREAM?: string;
  AZURE_TENANT_ID?: string;
  AZURE_CLIENT_ID?: string;
  AZURE_CLIENT_SECRET?: string;
  POSTHOG_TOKEN?: string;
}

// Entra client-credentials token, cached for the isolate's lifetime and
// refreshed ~5 minutes before expiry. A CF Worker cannot hold a managed
// identity — this app registration has exactly one role: Monitoring Metrics
// Publisher on the edge-logs DCR (infra/modules/edge-logs.bicep).
let tok: { value: string | null; exp: number } = { value: null, exp: 0 };

async function azureToken(env: Env): Promise<string | null> {
  const now = Date.now();
  if (tok.value && now < tok.exp - 300_000) {
    return tok.value;
  }
  const body = new URLSearchParams({
    client_id: env.AZURE_CLIENT_ID ?? "",
    client_secret: env.AZURE_CLIENT_SECRET ?? "",
    grant_type: "client_credentials",
    scope: "https://monitor.azure.com/.default",
  });
  const r = await fetch(
    `https://login.microsoftonline.com/${env.AZURE_TENANT_ID}/oauth2/v2.0/token`,
    {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body,
    },
  );
  const j = (await r.json()) as { access_token?: string; expires_in?: number };
  tok = {
    value: j.access_token ?? null,
    exp: now + (j.expires_in ?? 3600) * 1000,
  };
  return tok.value;
}

async function sendToAzure(
  env: Env,
  row: Record<string, unknown>,
): Promise<void> {
  const token = await azureToken(env);
  if (!token) {
    return;
  }
  const url =
    `${env.AZURE_DCE_ENDPOINT}/dataCollectionRules/${env.AZURE_DCR_ID}` +
    `/streams/${env.AZURE_STREAM}?api-version=2023-01-01`;
  await fetch(url, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify([row]),
  });
}

async function sendToPostHog(
  env: Env,
  event: Record<string, unknown>,
): Promise<void> {
  await fetch(`${env.POSTHOG_HOST}/i/v0/e/`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ api_key: env.POSTHOG_TOKEN, ...event }),
  });
}

export default {
  async fetch(
    req: Request,
    env: Env,
    ctx: ExecutionContext,
  ): Promise<Response> {
    const res = await fetch(req); // origin answer wins, always
    const path = new URL(req.url).pathname;
    if (isWatched(path)) {
      const bot = resolveBot(
        req.cf as EdgeCf | undefined,
        req.headers.get("x-kit-verified-bot"),
        req.headers.get("x-kit-verified-bot-category"),
      );
      // fail-open: a malformed/missing Date header must never throw.
      const d = new Date(res.headers.get("date") ?? Date.now());
      const ts = Number.isNaN(d.getTime())
        ? new Date().toISOString()
        : d.toISOString();
      const rec = buildRecord({
        path,
        method: req.method,
        status: res.status,
        ua: req.headers.get("user-agent"),
        ip: req.headers.get("cf-connecting-ip"),
        cf: { ...(req.cf as EdgeCf | undefined), botManagement: bot },
        ts,
      });
      try {
        env.EDGE_HITS.writeDataPoint(toAnalyticsEngine(rec));
      } catch {
        // fail-open: never let logging break the response
      }
      if (env.AZURE_ENABLED === "true") {
        ctx.waitUntil(sendToAzure(env, toLogAnalytics(rec)).catch(() => {}));
      }
      if (env.POSTHOG_ENABLED === "true") {
        ctx.waitUntil(sendToPostHog(env, toPostHog(rec)).catch(() => {}));
      }
    }
    return res;
  },
} satisfies ExportedHandler<Env>;
