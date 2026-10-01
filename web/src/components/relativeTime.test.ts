// The one relative-time wording in the app (EXP-83). Every case states its own clock, so none of
// these depends on when the suite runs.
import { describe, expect, it } from "vitest";
import { relativeTime } from "./relativeTime";

/** A local wall-clock time, so a case reads as the moment it means rather than as a UTC offset. */
const local = (y: number, m: number, d: number, h = 12, min = 0) =>
  new Date(y, m - 1, d, h, min);
const iso = (...args: Parameters<typeof local>) => local(...args).toISOString();

const now = local(2026, 3, 18, 12).getTime();

describe("how long ago", () => {
  it("names the coarsest unit that still says something", () => {
    expect(relativeTime(iso(2026, 3, 18, 11, 59), now)).toBe("1 minute ago");
    expect(relativeTime(iso(2026, 3, 18, 11, 15), now)).toBe("45 minutes ago");
    expect(relativeTime(iso(2026, 3, 18, 9), now)).toBe("3 hours ago");
    expect(relativeTime(iso(2026, 3, 16, 12), now)).toBe("2 days ago");
  });

  it("keeps counting in months and years rather than in hundreds of days", () => {
    expect(relativeTime(iso(2026, 1, 14, 12), now)).toBe("2 months ago");
    expect(relativeTime(iso(2025, 3, 18, 12), now)).toBe("1 year ago");
  });

  it("stays numeric: a day ago is a day ago, not yesterday", () => {
    expect(relativeTime(iso(2026, 3, 17, 12), now)).toBe("1 day ago");
  });
});

describe("how long until", () => {
  it("reads as a wait, in the same units", () => {
    expect(relativeTime(iso(2026, 3, 18, 12, 45), now)).toBe("in 45 minutes");
    expect(relativeTime(iso(2026, 3, 18, 17), now)).toBe("in 5 hours");
    expect(relativeTime(iso(2026, 3, 21, 12), now)).toBe("in 3 days");
  });
});

describe("the minute around now", () => {
  it("says now rather than counting seconds", () => {
    expect(relativeTime(iso(2026, 3, 18, 12), now)).toBe("now");
    expect(relativeTime(new Date(now - 59_000).toISOString(), now)).toBe("now");
  });

  it("says now rather than a negative number when the clocks disagree", () => {
    expect(relativeTime(new Date(now + 5_000).toISOString(), now)).toBe("now");
  });
});
