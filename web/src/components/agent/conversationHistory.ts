// The arithmetic and the wording behind the Roster Q&A history drawer (EXP-34, ADR §7).
//
// Kept out of the components on purpose: grouping, the expiry countdown and the resume window are
// the three things a reader of this feature would want to check against the ADR, and none of them
// needs a DOM to be true. Every function takes `now` so a test states the clock it means rather
// than arranging one — a spec that fakes timers to ask "is this Today?" is testing the fake.
import type { ConversationSummary } from "../../api";

const DAY_MS = 24 * 60 * 60 * 1000;

/** The two masked turns, verbatim. Both say *what kind* of gap this is and nothing more: the
 * detail is exactly what erasure and pause removed. */
export const REMOVED_TEXT = "Removed: referred to someone whose data was erased.";
export const HIDDEN_TEXT = "Hidden: referred to someone who has paused their profile.";

/** The drawer's footer sentence — the retention promise §6 of the ADR makes, in the one place a
 * person can act on it. */
export const RETENTION_NOTE = "Conversations are deleted 6 months after their last activity.";

/** How close to deletion a conversation has to be before the drawer says so. A countdown on every
 * row would be noise for five and a half of the six months. */
export const EXPIRY_HINT_DAYS = 14;

/** How long after its last turn a conversation is still "the one you were in" — the same 30
 * minutes the in-memory thread store used to expire on, kept as a *resume* rule now that nothing
 * expires (ADR §4). Past it, opening the dock starts fresh rather than re-opening yesterday. */
export const RESUME_WINDOW_MS = 30 * 60 * 1000;

export type HistoryGroup = "Today" | "This week" | "Older";

/** In reading order, which is also the order the drawer renders them in. */
export const HISTORY_GROUPS: readonly HistoryGroup[] = ["Today", "This week", "Older"];

/** Local midnight `offsetDays` from the one containing `now`. Built from the calendar fields
 * rather than by subtracting milliseconds, so a DST boundary inside the week does not shift the
 * group edges by an hour. */
function localMidnight(now: Date, offsetDays: number): number {
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + offsetDays).getTime();
}

/**
 * Which heading a conversation sits under, by **local calendar day** — "Today" means today's
 * date where the reader is, not "within the last 24 hours". The two differ for most of the day and
 * the calendar reading is the one a person checking "did I ask this this morning?" expects.
 */
export function groupOf(lastActiveAt: string, now: Date = new Date()): HistoryGroup {
  const at = new Date(lastActiveAt).getTime();
  if (at >= localMidnight(now, 0)) return "Today";
  if (at >= localMidnight(now, -6)) return "This week";
  return "Older";
}

/** Whole days left before retention deletes this conversation, never negative. Rounded up, so a
 * conversation with six hours left says "1 d" rather than "0 d" — the promise is about the day it
 * disappears, and the sweep is daily. */
export function daysUntil(expiresAt: string, now: number = Date.now()): number {
  return Math.max(0, Math.ceil((new Date(expiresAt).getTime() - now) / DAY_MS));
}

/** Whether the drawer should warn about this conversation's expiry at all. */
export function isExpiringSoon(expiresAt: string, now: number = Date.now()): boolean {
  return daysUntil(expiresAt, now) <= EXPIRY_HINT_DAYS;
}

/**
 * How long ago, in the coarsest unit that still says something. Falls back to a plain local date
 * past a month, because "63 d ago" is arithmetic the reader has to undo.
 */
export function relativeTime(at: string, now: number = Date.now()): string {
  const when = new Date(at).getTime();
  const minutes = Math.round((now - when) / 60_000);
  // Also the future: a clock a few seconds ahead of the server's is not worth a negative number.
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.round(hours / 24);
  if (days < 30) return `${days} d ago`;
  return new Date(at).toLocaleDateString();
}

/**
 * The conversation the surface should re-open on load, or `null` for a fresh one. The list arrives
 * most-recently-active first, so this is the head of it and only while it is still inside the
 * resume window.
 */
export function conversationToResume(
  conversations: readonly ConversationSummary[] | undefined,
  now: number = Date.now(),
): ConversationSummary | null {
  const latest = conversations?.[0];
  if (!latest) return null;
  return now - new Date(latest.lastActiveAt).getTime() <= RESUME_WINDOW_MS ? latest : null;
}
