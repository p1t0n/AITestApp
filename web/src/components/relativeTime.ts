// The app's one relative-time wording (EXP-83). Two surfaces used to hand-roll it — the history
// drawer counting backwards ("2 d ago") and the usage panel counting forwards ("in 3d") — with two
// unit ladders, two sets of abbreviations and two different answers for the minute around now.
//
// `Intl.RelativeTimeFormat` already knows the wording in both directions, including the plural, so
// what is left here is only the choice of unit. The locale is pinned to `en` rather than taken
// from the runtime: every other string this app renders is an English literal, and a caption that
// changes language with the browser while the sentence around it does not is worse than one that
// does not change at all. It also keeps the suite asserting one output everywhere it runs.

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
const MONTH = 30 * DAY;
const YEAR = 365 * DAY;

/** Below this, a count of seconds is noise: both callers want the same single word. */
const NOW_WINDOW = MINUTE;

/** Coarsest-first is wrong here — the first unit the gap *fits inside* is the one to say it in. */
const UNITS: readonly (readonly [limit: number, size: number, unit: Intl.RelativeTimeFormatUnit])[] =
  [
    [HOUR, MINUTE, "minute"],
    [DAY, HOUR, "hour"],
    [MONTH, DAY, "day"],
    [YEAR, MONTH, "month"],
    [Infinity, YEAR, "year"],
  ];

// `numeric: "always"`, so a day ago reads "1 day ago" and not "yesterday": these captions sit next
// to headings that already say which day it was, and "yesterday" under "This week" says less.
const FORMAT = new Intl.RelativeTimeFormat("en", { numeric: "always" });

/**
 * How far `at` is from `now`, in the coarsest unit that still says something — "3 hours ago" for a
 * past moment, "in 3 days" for a future one, and "now" for anything inside a minute either way.
 *
 * The future side is not only for deadlines: a client clock a few seconds ahead of the server's
 * would otherwise turn a just-written turn into a negative number.
 */
export function relativeTime(at: string, now: number = Date.now()): string {
  const delta = new Date(at).getTime() - now;
  if (Math.abs(delta) < NOW_WINDOW) return "now";

  const [, size, unit] = UNITS.find(([limit]) => Math.abs(delta) < limit)!;
  return FORMAT.format(Math.round(delta / size), unit);
}
