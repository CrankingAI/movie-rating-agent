import { describe, expect, it } from "vitest";
import {
  buildRecord,
  isWatched,
  resolveBot,
  toAnalyticsEngine,
  toLogAnalytics,
  toPostHog,
} from "./record";

const cfVerified = {
  country: "US",
  regionCode: "MA",
  city: "Boston",
  continent: "NA",
  timezone: "America/New_York",
  colo: "BOS",
  botManagement: { verifiedBot: true, verifiedBotCategory: "AI Crawler" },
};
const cfHuman = {
  country: "DE",
  regionCode: "BE",
  city: "Berlin",
  continent: "EU",
  timezone: "Europe/Berlin",
  colo: "TXL",
  botManagement: { verifiedBot: false },
};

const base = { method: "GET", status: 200, ts: "2026-07-04T12:00:00.000Z" };

const verifiedRec = buildRecord({
  ...base,
  path: "/llms.txt",
  ua: "GPTBot/1.2",
  ip: "203.0.113.7",
  cf: cfVerified,
});
const humanRec = buildRecord({
  ...base,
  path: "/security.txt",
  ua: "curl/8.9",
  ip: "198.51.100.4",
  cf: cfHuman,
});

const cfVerifiedNoCategory = {
  country: "GB",
  regionCode: "LDN",
  city: "London",
  continent: "EU",
  timezone: "Europe/London",
  colo: "LHR",
  botManagement: { verifiedBot: true },
};
const verifiedNoCategoryRec = buildRecord({
  ...base,
  path: "/llms.txt",
  ua: "SomeBot/1.0",
  ip: "203.0.113.9",
  cf: cfVerifiedNoCategory,
});

describe("resolveBot — free-plan header fallback vs Enterprise botManagement", () => {
  it("header 'true' + category, no botManagement -> verified, category flows into buildRecord/toAnalyticsEngine", () => {
    const bot = resolveBot(undefined, "true", "AI Crawler");
    expect(bot).toEqual({
      verifiedBot: true,
      verifiedBotCategory: "AI Crawler",
    });
    const rec = buildRecord({
      ...base,
      path: "/llms.txt",
      ua: "GPTBot/1.2",
      ip: "203.0.113.7",
      cf: { botManagement: bot },
    });
    expect(rec.verified).toBe(true);
    expect(rec.botName).toBe("AI Crawler");
    expect(toAnalyticsEngine(rec).blobs).toContain("GPTBot/1.2");
  });

  it("headers null/absent -> unverified", () => {
    const bot = resolveBot(undefined, null, null);
    expect(bot).toEqual({ verifiedBot: false, verifiedBotCategory: undefined });
  });

  it("botManagement present wins over contradictory headers", () => {
    const bot = resolveBot(
      { botManagement: { verifiedBot: false } },
      "true",
      "AI Crawler",
    );
    expect(bot).toEqual({ verifiedBot: false });
  });
});

describe("isWatched", () => {
  it("matches exact files and the well-known prefix", () => {
    expect(isWatched("/llms.txt")).toBe(true);
    expect(isWatched("/.well-known/security.txt")).toBe(true);
    expect(isWatched("/pricing")).toBe(false);
    expect(isWatched("/llms.txt.bak")).toBe(false);
  });
});

describe("DNT split — Analytics Engine (long-lived, DNT-safe)", () => {
  it("never emits the client IP", () => {
    for (const rec of [verifiedRec, humanRec]) {
      const p = toAnalyticsEngine(rec);
      expect(JSON.stringify(p)).not.toContain(rec.ip);
    }
  });
  it("emits UA only for verified bots", () => {
    expect(toAnalyticsEngine(verifiedRec).blobs).toContain("GPTBot/1.2");
    expect(JSON.stringify(toAnalyticsEngine(humanRec))).not.toContain(
      "curl/8.9",
    );
  });
  it("keeps path as the single index and status as a double", () => {
    const p = toAnalyticsEngine(verifiedRec);
    expect(p.indexes).toEqual(["/llms.txt"]);
    expect(p.doubles[0]).toBe(200);
  });
});

describe("DNT split — Log Analytics (10-day, full fidelity)", () => {
  it("carries IP and UA for every hit, with exactly the Bicep column keys", () => {
    const row = toLogAnalytics(humanRec);
    expect(row).toEqual({
      TimeGenerated: "2026-07-04T12:00:00.000Z",
      Path: "/security.txt",
      Method: "GET",
      Status: 200,
      UserAgent: "curl/8.9",
      Ip: "198.51.100.4",
      Country: "DE",
      Region: "BE",
      City: "Berlin",
      Colo: "TXL",
      Verified: false,
      BotName: "",
      Score: -1,
    });
  });
});

describe("DNT split — PostHog (DNT-safe, geo-aware)", () => {
  it("stamps $geoip_* from request.cf and disables ingest GeoIP", () => {
    const ev = toPostHog(humanRec) as { properties: Record<string, unknown> };
    expect(ev.properties.$geoip_disable).toBe(true);
    expect(ev.properties.$geoip_country_code).toBe("DE");
    expect(ev.properties.$geoip_city_name).toBe("Berlin");
    expect(ev.properties.$geoip_subdivision_1_code).toBe("BE");
    expect(ev.properties.$process_person_profile).toBe(false);
  });
  it("never carries the IP; UA only when verified", () => {
    expect(JSON.stringify(toPostHog(humanRec))).not.toContain("198.51.100.4");
    expect(JSON.stringify(toPostHog(humanRec))).not.toContain("curl/8.9");
    const ev = toPostHog(verifiedRec) as {
      properties: Record<string, unknown>;
    };
    expect(ev.properties.user_agent).toBe("GPTBot/1.2");
  });
  it("identifies by bot name, never by user identity", () => {
    expect(
      (toPostHog(verifiedRec) as { distinct_id: string }).distinct_id,
    ).toBe("AI Crawler");
    expect((toPostHog(humanRec) as { distinct_id: string }).distinct_id).toBe(
      "unverified-traffic",
    );
  });
  it("falls back to 'verified-bot' when verified but no bot category", () => {
    expect(
      (toPostHog(verifiedNoCategoryRec) as { distinct_id: string }).distinct_id,
    ).toBe("verified-bot");
  });
});
