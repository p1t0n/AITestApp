// Pure stepper state for the staffing SSE stream — no React, unit-testable in isolation.
import type { StaffingStepEvent } from "../../api";

export type StaffingStageView = "pending" | "active" | "done" | "failed";

export interface StaffingProgress {
  shortlist: StaffingStageView;
  match: StaffingStageView;
  matchCompleted: number;
  matchTotal: number | null;
  /** One entry per finished match run, in completion order; failed ones get an inline warning. */
  matchTicks: { name: string; failed: boolean }[];
  narrative: StaffingStageView;
  narrativeError: string | null;
}

export const STAFFING_IDLE: StaffingProgress = {
  shortlist: "pending",
  match: "pending",
  matchCompleted: 0,
  matchTotal: null,
  matchTicks: [],
  narrative: "pending",
  narrativeError: null,
};

/** Folds one step/stepFailed event into the stepper. Events arrive in run order, so a later
 * stage's first event also settles the stages before it (e.g. a match event marks shortlist done).
 * A failed match run warns inline but never fails the stage — completedCount counts it, and the
 * run continues under the degrade policy. Cap-skipped stages emit no events and stay pending. */
export function reduceStaffingStep(p: StaffingProgress, evt: StaffingStepEvent): StaffingProgress {
  const next: StaffingProgress = { ...p };
  switch (evt.stage) {
    case "shortlist":
      next.shortlist =
        evt.status === "started" ? "active" : evt.status === "completed" ? "done" : "failed";
      break;
    case "match": {
      if (next.shortlist !== "failed") next.shortlist = "done";
      if (evt.totalCount != null) next.matchTotal = evt.totalCount;
      if (evt.status !== "started") {
        if (evt.completedCount != null) next.matchCompleted = evt.completedCount;
        if (evt.candidate) {
          next.matchTicks = [
            ...p.matchTicks,
            { name: evt.candidate.name, failed: evt.status === "failed" },
          ];
        }
      }
      next.match =
        next.matchTotal != null && next.matchCompleted >= next.matchTotal ? "done" : "active";
      break;
    }
    case "narrative":
      if (next.match === "active") next.match = "done";
      if (evt.status === "started") next.narrative = "active";
      else if (evt.status === "completed") next.narrative = "done";
      else {
        next.narrative = "failed";
        next.narrativeError = evt.error ?? null;
      }
      break;
  }
  return next;
}

/** The terminal report settles every stage: anything still pending/active is done; a stage that
 * warned (narrative failed) keeps its warning. */
export function settleStaffingProgress(p: StaffingProgress): StaffingProgress {
  return {
    ...p,
    shortlist: p.shortlist === "failed" ? "failed" : "done",
    match: p.match === "failed" ? "failed" : "done",
    narrative: p.narrative === "failed" ? "failed" : "done",
  };
}
