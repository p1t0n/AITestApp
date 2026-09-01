// The staffing pipeline (P1T-71/76). POST /agents/staffing streams Server-Sent Events:
// step/stepFailed progress frames for a stepper UI, then exactly one terminal frame — the full
// report or a problem-style error. Pre-stream failures (400/401/429) surface as a thrown
// SseHttpError before any event arrives.
//
// The axios clients buffer whole responses, so this one surface rides the fetch-based SSE helper
// instead. It is the only imperative, non-hook call in the data layer, and the only one that takes
// an AbortSignal.
//
// The persisted proposal this run creates, and the approver's decision on it, live in ./proposals.
import type { ShortlistCoverage, ShortlistRequirementItem } from "./shortlist";
import type { JdExtraction } from "./shared";
import { postSse } from "../../sse";

export interface StaffingRequest {
  jobDescription: string;
  availableOn?: string; // ISO date (yyyy-MM-dd)
  skillIds?: string[];
  location?: string;
  minYears?: number;
  matchTop?: number; // 1..5, server default 3
}

export type StaffingStage = "shortlist" | "match" | "narrative";
export type StaffingStepStatus = "started" | "completed" | "failed";

/** One `step`/`stepFailed` frame. Match-stage frames carry the candidate and k/N counters;
 * `error` is set only on `stepFailed` (the run continues under the degrade policy). */
export interface StaffingStepEvent {
  stage: StaffingStage;
  status: StaffingStepStatus;
  candidate?: { expertId: string; name: string };
  completedCount?: number;
  totalCount?: number;
  error?: string;
}

export type StaffingMatchStatus = "completed" | "failed" | "skipped";

/** One candidate's match-step result. Score/band are parsed from the answer markdown and can be
 * null even when completed (the markdown ships regardless); `error` is set only on failure. */
export interface StaffingMatchResult {
  status: StaffingMatchStatus;
  score?: number | null;
  band?: string | null;
  answer?: string | null;
  error?: string | null;
}

export interface StaffingReportCandidate {
  expertId: string;
  name: string;
  title: string;
  shortlist: {
    score: number;
    coverage: ShortlistCoverage;
    requirements: ShortlistRequirementItem[];
  };
  match: StaffingMatchResult;
  rationale: string;
}

/** The pinned staffing report (P1T-71). `recommendation` is absent when the narrative degraded;
 * `degraded` + `notes` explain any partial results. `proposalId` (P1T-100) references the pending
 * approval record; absent when the run couldn't persist one. */
export interface StaffingReport {
  requirements: string[];
  candidates: StaffingReportCandidate[];
  recommendation?: { expertId: string; narrative: string } | null;
  degraded: boolean;
  notes: string[];
  proposalId?: string | null;
  extraction?: JdExtraction | null;
}

/** The terminal `error` frame (failed shortlist or an unexpected fault). */
export interface StaffingTerminalError {
  title: string;
  detail: string;
}

export interface StaffingRunHandlers {
  /** Every `step` and `stepFailed` frame, in order (`stepFailed` arrives with status "failed"). */
  onStep: (event: StaffingStepEvent) => void;
  /** The terminal report frame. */
  onReport: (report: StaffingReport) => void;
  /** The terminal error frame. */
  onError: (error: StaffingTerminalError) => void;
}

/**
 * Runs one staffing pipeline over SSE. Resolves when the stream closes (after a terminal frame);
 * rejects with SseHttpError on pre-stream HTTP failures (e.g. the 429 cap body) and with the
 * abort error when `signal` cancels the run mid-stream.
 */
export async function runStaffing(
  req: StaffingRequest,
  handlers: StaffingRunHandlers,
  signal?: AbortSignal,
): Promise<void> {
  await postSse(
    "/agents/staffing",
    req,
    (message) => {
      switch (message.event) {
        case "step":
        case "stepFailed":
          handlers.onStep(JSON.parse(message.data) as StaffingStepEvent);
          break;
        case "report":
          handlers.onReport(JSON.parse(message.data) as StaffingReport);
          break;
        case "error":
          handlers.onError(JSON.parse(message.data) as StaffingTerminalError);
          break;
      }
    },
    signal,
  );
}
