// Pure record shaping + per-sink field splits for the edge logging Worker.
// The DNT split is enforced HERE, in one place: sinks whose retention cannot
// be bounded to 10 days (Analytics Engine, PostHog) never receive the client
// IP, and receive the User-Agent only for verified-bot hits (a verified
// crawler is not a DNT "user"). The Log Analytics sink (10-day EdgeHits_CL
// table) gets everything. Governing convention: docs/edge.md.

export const WATCHED = [
  "/sitemap.xml",
  "/llms.txt",
  "/llms-full.txt",
  "/robots.txt",
  "/security.txt",
  "/.well-known/",
] as const;

export function isWatched(path: string): boolean {
  return WATCHED.some((p) =>
    p.endsWith("/") ? path.startsWith(p) : path === p,
  );
}

// request.cf.botManagement is Enterprise Bot Management only; on other plans the
// zone transform rule (cf-zone-init.sh) stamps x-kit-verified-bot[-category]
// from cf.client.bot / cf.verified_bot_category — re-verify at adoption.
export interface EdgeCfBot {
  verifiedBot?: boolean;
  verifiedBotCategory?: string;
  score?: number; // Enterprise Bot Management only
}

export interface EdgeCf {
  country?: string;
  regionCode?: string;
  city?: string;
  continent?: string;
  timezone?: string;
  colo?: string;
  botManagement?: EdgeCfBot;
}

// Enterprise's request.cf.botManagement (richest source, includes score) wins
// when present; every other plan falls back to the zone transform rule's
// headers (cf-zone-init.sh), sourced from cf.client.bot / cf.verified_bot_category.
export function resolveBot(
  cf: EdgeCf | undefined,
  headerVerified: string | null,
  headerCategory: string | null,
): EdgeCfBot {
  if (cf?.botManagement) {
    return cf.botManagement;
  }
  return {
    verifiedBot: headerVerified === "true",
    verifiedBotCategory: headerCategory ?? undefined,
  };
}

export interface EdgeRecord {
  ts: string;
  path: string;
  method: string;
  status: number;
  ua: string;
  ip: string;
  country: string;
  regionCode: string;
  city: string;
  continent: string;
  timezone: string;
  colo: string;
  verified: boolean;
  botName: string;
  score: number;
}

export function buildRecord(args: {
  path: string;
  method: string;
  status: number;
  ua: string | null;
  ip: string | null;
  cf: EdgeCf | undefined;
  ts: string;
}): EdgeRecord {
  const cf = args.cf ?? {};
  const bot = cf.botManagement ?? {};
  return {
    ts: args.ts,
    path: args.path,
    method: args.method,
    status: args.status,
    ua: args.ua ?? "",
    ip: args.ip ?? "",
    country: cf.country ?? "",
    regionCode: cf.regionCode ?? "",
    city: cf.city ?? "",
    continent: cf.continent ?? "",
    timezone: cf.timezone ?? "",
    colo: cf.colo ?? "",
    verified: bot.verifiedBot === true,
    botName: bot.verifiedBotCategory ?? "",
    score: typeof bot.score === "number" ? bot.score : -1,
  };
}

// Analytics Engine (~3-month retention, not shortenable): DNT-safe only.
// Blob positions are load-bearing — docs/edge.md's SQL examples index them.
export function toAnalyticsEngine(rec: EdgeRecord): {
  indexes: string[];
  blobs: string[];
  doubles: number[];
} {
  return {
    indexes: [rec.path], // 1 index, ≤96 bytes
    blobs: [
      rec.path, // blob1
      rec.verified ? rec.ua : "", // blob2 — UA only for verified bots
      rec.country, // blob3
      rec.colo, // blob4
      rec.verified ? "verified" : "unverified", // blob5
      rec.botName, // blob6
      rec.method, // blob7
    ], // never the IP
    doubles: [rec.status, rec.score],
  };
}

// Log Analytics EdgeHits_CL (10-day retention): full fidelity, DNT-bounded.
// Keys MUST match the DCR stream / table columns in infra/modules/edge-logs.bicep.
export function toLogAnalytics(rec: EdgeRecord): Record<string, unknown> {
  return {
    TimeGenerated: rec.ts,
    Path: rec.path,
    Method: rec.method,
    Status: rec.status,
    UserAgent: rec.ua,
    Ip: rec.ip,
    Country: rec.country,
    Region: rec.regionCode,
    City: rec.city,
    Colo: rec.colo,
    Verified: rec.verified,
    BotName: rec.botName,
    Score: rec.score,
  };
}

// PostHog (retention per workspace): DNT-safe, geo-aware via $geoip_* stamped
// from request.cf with ingest GeoIP disabled (else PostHog resolves the
// Worker's egress IP). Granularity capped at country/region + city.
export function toPostHog(rec: EdgeRecord): Record<string, unknown> {
  const properties: Record<string, unknown> = {
    $process_person_profile: false, // bots and anonymous hits — no "persons"
    $geoip_disable: true,
    $geoip_country_code: rec.country,
    $geoip_subdivision_1_code: rec.regionCode,
    $geoip_city_name: rec.city,
    $geoip_continent_code: rec.continent,
    $geoip_time_zone: rec.timezone,
    path: rec.path,
    status: rec.status,
    verified_bot: rec.verified,
    bot_name: rec.botName,
    bot_score: rec.score,
  };
  if (rec.verified) {
    properties.user_agent = rec.ua;
  }
  return {
    event: "edge_fetch",
    distinct_id:
      rec.botName || (rec.verified ? "verified-bot" : "unverified-traffic"),
    timestamp: rec.ts,
    properties,
  };
}
