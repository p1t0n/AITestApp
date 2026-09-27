// PROTOTYPE — throwaway (branch prototype/dashboard). Not production code: no tests, no error paths.
//
// The dashboard question needs every Expert's availability *timeline*, and the only bulk read the
// API has (`/experts?includeDrafts=true`) carries today's capacity and nothing else. So this fans
// out one `/experts/:id` per row, eight at a time, and derives everything client-side. That is
// fine against a local 500-row roster and is the first thing a real build must replace with one
// server-side aggregate (see FINDINGS in DashboardPrototype.tsx).
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { http } from "../../api/http";
import type { AvailabilityEntry, ExpertDetail, ExpertSummary } from "../../types";

export interface DashExpert {
  id: string;
  name: string;
  title: string;
  location: string;
  status: string;
  paused: boolean;
  entries: AvailabilityEntry[];
  skills: string[];
  /** Average capacity per month of `months`, 0-100. */
  monthly: number[];
  today: number;
}

/** The step function, same rule as `CapacityCalculator.CapacityOn`: 0 before the first entry. */
export function capacityOn(entries: AvailabilityEntry[], day: Date): number {
  const iso = day.toISOString().slice(0, 10);
  let best: AvailabilityEntry | null = null;
  for (const e of entries) {
    if (e.effectiveFrom <= iso && (best === null || e.effectiveFrom > best.effectiveFrom)) best = e;
  }
  return best?.capacityPercent ?? 0;
}

/** Day-weighted average capacity across one calendar month — a step on the 20th counts for 1/3. */
export function monthAverage(entries: AvailabilityEntry[], year: number, month: number): number {
  const days = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  let sum = 0;
  for (let d = 1; d <= days; d++) sum += capacityOn(entries, new Date(Date.UTC(year, month, d)));
  return Math.round(sum / days);
}

export interface Month {
  year: number;
  month: number;
  label: string;
}

/** `count` months starting at the first of `start`'s month. */
export function monthsFrom(start: Date, count: number): Month[] {
  return Array.from({ length: count }, (_, i) => {
    const d = new Date(Date.UTC(start.getUTCFullYear(), start.getUTCMonth() + i, 1));
    return {
      year: d.getUTCFullYear(),
      month: d.getUTCMonth(),
      label: d.toLocaleString("en", { month: "short", timeZone: "UTC" }) + (d.getUTCMonth() === 0 ? ` ${String(d.getUTCFullYear()).slice(2)}` : ""),
    };
  });
}

export const TODAY = new Date();
/** Admin horizon: this month + the next 11. */
export const ADMIN_MONTHS = monthsFrom(TODAY, 12);

async function pool<T, R>(items: T[], size: number, fn: (t: T) => Promise<R>, onDone: () => void): Promise<R[]> {
  const out: R[] = new Array(items.length);
  let next = 0;
  await Promise.all(
    Array.from({ length: size }, async () => {
      while (next < items.length) {
        const i = next++;
        out[i] = await fn(items[i]);
        onDone();
      }
    }),
  );
  return out;
}

export function useDashboardRoster() {
  const [progress, setProgress] = useState({ done: 0, total: 0 });
  const query = useQuery({
    queryKey: ["prototype", "dashboard"],
    staleTime: Infinity,
    queryFn: async (): Promise<DashExpert[]> => {
      const summaries = (await http.get<ExpertSummary[]>("/experts", { params: { includeDrafts: true } })).data;
      let done = 0;
      setProgress({ done: 0, total: summaries.length });
      const details = await pool(
        summaries,
        8,
        async (s) => (await http.get<ExpertDetail>(`/experts/${s.id}`)).data,
        () => setProgress({ done: ++done, total: summaries.length }),
      );
      return details.map((d) => ({
        id: d.id,
        name: `${d.firstName} ${d.lastName}`,
        title: d.title,
        location: d.location ?? "Unknown",
        status: d.status,
        paused: !!d.hiddenAt,
        entries: d.availabilityEntries,
        skills: d.skills.map((s) => s.skillName),
        monthly: ADMIN_MONTHS.map((m) => monthAverage(d.availabilityEntries, m.year, m.month)),
        today: d.currentCapacityPercent,
      }));
    },
  });
  return { ...query, progress };
}

// ---- derived gauges -------------------------------------------------------------------------

/** On the bench = Active and not paused; Drafts and paused rows are not staffable. */
export const staffable = (e: DashExpert) => e.status === "Active" && !e.paused;

export function band(c: number): "full" | "partial" | "none" {
  return c >= 100 ? "full" : c > 0 ? "partial" : "none";
}

/** Available FTE in month i = Σ capacity/100 over staffable experts. */
export function fteByMonth(experts: DashExpert[]): number[] {
  return ADMIN_MONTHS.map((_, i) => experts.filter(staffable).reduce((s, e) => s + e.monthly[i] / 100, 0));
}

/** Experts whose capacity goes *up* within `days` — about to come free. */
export function freeingSoon(experts: DashExpert[], days: number) {
  const now = TODAY.toISOString().slice(0, 10);
  const until = new Date(TODAY.getTime() + days * 86400000).toISOString().slice(0, 10);
  return experts
    .filter(staffable)
    .flatMap((e) => {
      const step = [...e.entries]
        .sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom))
        .find((x) => x.effectiveFrom > now && x.effectiveFrom <= until && x.capacityPercent > e.today);
      return step ? [{ expert: e, on: step.effectiveFrom, to: step.capacityPercent }] : [];
    })
    .sort((a, b) => a.on.localeCompare(b.on));
}

export function byLocation(experts: DashExpert[]) {
  const m = new Map<string, DashExpert[]>();
  for (const e of experts) m.set(e.location, [...(m.get(e.location) ?? []), e]);
  return [...m.entries()].map(([location, list]) => ({ location, list })).sort((a, b) => b.list.length - a.list.length);
}

/** Top skills among staffable experts with capacity > 0 today. */
export function benchSkills(experts: DashExpert[], n: number) {
  const c = new Map<string, number>();
  for (const e of experts.filter((x) => staffable(x) && x.today > 0)) for (const s of e.skills) c.set(s, (c.get(s) ?? 0) + 1);
  return [...c.entries()].sort((a, b) => b[1] - a[1]).slice(0, n);
}
