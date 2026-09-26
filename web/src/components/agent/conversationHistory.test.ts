// The history drawer's arithmetic (EXP-34). Every case states its own clock, so none of these
// depends on when the suite runs — which is the failure mode a date helper invites.
import { describe, expect, it } from "vitest";
import type { ConversationSummary } from "../../api";
import {
  EXPIRY_HINT_DAYS,
  conversationToResume,
  daysUntil,
  groupOf,
  isExpiringSoon,
  relativeTime,
} from "./conversationHistory";

/** A local wall-clock time, so the calendar-day rules are asserted in the zone they are about. */
const local = (
  y: number, m: number, d: number, h = 12, min = 0,
) => new Date(y, m - 1, d, h, min);

const iso = (...args: Parameters<typeof local>) => local(...args).toISOString();

describe("grouping by local calendar day", () => {
  const now = local(2026, 3, 18, 14, 30); // a Wednesday afternoon

  it("puts anything since local midnight under Today", () => {
    expect(groupOf(iso(2026, 3, 18, 0, 1), now)).toBe("Today");
    expect(groupOf(iso(2026, 3, 18, 14, 29), now)).toBe("Today");
  });

  it("does not call 20 hours ago Today when it fell on yesterday's date", () => {
    // The whole reason this is a calendar rule and not a 24-hour one: 18:30 *yesterday* is well
    // inside 24 hours and is still not "today" to the person who asked it.
    expect(groupOf(iso(2026, 3, 17, 18, 30), now)).toBe("This week");
  });

  it("keeps the previous six days under This week, and drops the seventh to Older", () => {
    expect(groupOf(iso(2026, 3, 12, 23, 59), now)).toBe("This week");
    expect(groupOf(iso(2026, 3, 11, 23, 59), now)).toBe("Older");
  });

  it("reads a clock a little ahead of ours as Today rather than as the future", () => {
    expect(groupOf(iso(2026, 3, 18, 14, 31), now)).toBe("Today");
  });
});

describe("the expiry countdown", () => {
  const now = local(2026, 3, 18, 12).getTime();

  it("rounds a part-day up, because the promise is about the day it disappears", () => {
    expect(daysUntil(iso(2026, 3, 18, 18), now)).toBe(1);
    expect(daysUntil(iso(2026, 3, 20, 12), now)).toBe(2);
  });

  it("never goes negative for something retention should already have taken", () => {
    expect(daysUntil(iso(2026, 3, 1, 12), now)).toBe(0);
  });

  it("warns at the fourteenth day and stays quiet on the fifteenth", () => {
    expect(EXPIRY_HINT_DAYS).toBe(14);
    expect(isExpiringSoon(iso(2026, 4, 1, 12), now)).toBe(true);   // 14 days
    expect(isExpiringSoon(iso(2026, 4, 2, 12), now)).toBe(false);  // 15 days
  });

  it("stays quiet for the five and a half months nothing is happening", () => {
    expect(isExpiringSoon(iso(2026, 9, 18, 12), now)).toBe(false);
  });
});

describe("relative time", () => {
  const now = local(2026, 3, 18, 12).getTime();

  it("names the coarsest unit that still says something", () => {
    expect(relativeTime(iso(2026, 3, 18, 11, 59), now)).toBe("1 min ago");
    expect(relativeTime(iso(2026, 3, 18, 9), now)).toBe("3 h ago");
    expect(relativeTime(iso(2026, 3, 16, 12), now)).toBe("2 d ago");
  });

  it("says just now rather than a negative number when the clocks disagree", () => {
    expect(relativeTime(iso(2026, 3, 18, 12, 0), now)).toBe("just now");
    expect(relativeTime(iso(2026, 3, 18, 12, 5), now)).toBe("just now");
  });

  it("falls back to a date past a month, rather than to arithmetic the reader has to undo", () => {
    const old = iso(2025, 11, 2, 12);
    expect(relativeTime(old, now)).toBe(new Date(old).toLocaleDateString());
  });
});

describe("which conversation the surface re-opens", () => {
  const now = local(2026, 3, 18, 12).getTime();
  const summary = (id: string, lastActiveAt: string): ConversationSummary => ({
    id,
    title: `conversation ${id}`,
    createdAt: lastActiveAt,
    lastActiveAt,
    expiresAt: iso(2026, 9, 18, 12),
  });

  it("re-opens the most recent one when it was active inside the last 30 minutes", () => {
    const list = [summary("a", iso(2026, 3, 18, 11, 40)), summary("b", iso(2026, 3, 17, 12))];
    expect(conversationToResume(list, now)?.id).toBe("a");
  });

  it("starts fresh when even the most recent one has gone cold", () => {
    expect(conversationToResume([summary("a", iso(2026, 3, 18, 11, 29))], now)).toBeNull();
  });

  it("starts fresh when there is nothing to re-open", () => {
    expect(conversationToResume([], now)).toBeNull();
    expect(conversationToResume(undefined, now)).toBeNull();
  });
});
