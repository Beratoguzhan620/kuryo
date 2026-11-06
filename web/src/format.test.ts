import { describe, expect, it } from "vitest";
import { distanceKm, initials, isFresh, relative, secondsLeft } from "./format";
import { parseRoute } from "./router";

describe("format", () => {
  it("measures straight-line distance like the server", () => {
    expect(distanceKm({ latitude: 41.2867, longitude: 36.33 }, { latitude: 41.33, longitude: 36.29 })).toBeCloseTo(5.9, 1);
  });

  it("counts seconds down and never below zero", () => {
    const now = Date.UTC(2026, 10, 6, 9, 0, 0);
    expect(secondsLeft(new Date(now + 12_400).toISOString(), now)).toBe(13);
    expect(secondsLeft(new Date(now - 5_000).toISOString(), now)).toBe(0);
  });

  it("trusts a location for two minutes", () => {
    const now = Date.UTC(2026, 10, 6, 9, 0, 0);
    expect(isFresh(new Date(now - 119_000).toISOString(), now)).toBe(true);
    expect(isFresh(new Date(now - 121_000).toISOString(), now)).toBe(false);
    expect(isFresh(null, now)).toBe(false);
  });

  it("writes times relative to now", () => {
    const now = Date.UTC(2026, 10, 6, 9, 0, 0);
    expect(relative(new Date(now + 12 * 60_000).toISOString(), now)).toBe("in 12 min");
    expect(relative(new Date(now - 10_000).toISOString(), now)).toBe("just now");
    expect(initials("ayşe yılmaz")).toBe("AY");
  });
});

describe("routes", () => {
  it("opens tracking links without an account and company pages by id", () => {
    expect(parseRoute("/t/AbC_123-xyz456789012")).toEqual({ page: "track", token: "AbC_123-xyz456789012" });
    expect(parseRoute("/t/<script>")).toEqual({ page: "not-found" });
    expect(parseRoute("/c/01a1b0d8-1289-730e-a027-06082fab18cc")).toEqual({ page: "live", companyId: "01a1b0d8-1289-730e-a027-06082fab18cc" });
    expect(parseRoute("/signin", "?next=//evil.example")).toEqual({ page: "signin", next: undefined });
  });
});
