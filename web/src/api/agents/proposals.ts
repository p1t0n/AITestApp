// The approval inbox and its drill-in (P1T-134/135) — the one human decision per staffing run.
//
// The inbox is the light index (deterministic snapshot columns, taken from the report at creation);
// the drill-in carries the full persisted handoff package, so an approver decides from the package
// alone and nothing re-runs. `package` is null on rows older than the package column (P1T-133).
import { useQuery } from "@tanstack/react-query";
import { agentHttp } from "../http";
import type { StaffingReport } from "./staffing";

/** A staffing proposal decision result (P1T-100). */
export interface StaffingProposalDecision {
  id: string;
  status: "pending" | "approved" | "rejected";
  decisionNote?: string | null;
}

/** Records the human decision on a staffing proposal — approve or reject, once. */
export async function decideStaffingProposal(
  proposalId: string,
  decision: "approved" | "rejected",
  note?: string,
): Promise<StaffingProposalDecision> {
  const res = await agentHttp.post<StaffingProposalDecision>(
    `/staffing/proposals/${proposalId}/decision`,
    { decision, note },
  );
  return res.data;
}

// ---- Approval inbox + drill-in (P1T-134/135) ----
// The inbox is the light index (snapshot columns); the drill-in carries the full persisted
// handoff package, so an approver decides from the package alone — nothing re-runs.

/** One candidate snapshot on an inbox row (deterministic, from the report at creation). */
export interface StaffingProposalCandidateSummary {
  expertId: string;
  name: string;
  title: string;
  rank: number;
  matchScore?: number | null;
  matchBand?: string | null;
  rationale: string;
}

/** One inbox row (P1T-100 index). */
export interface StaffingProposalSummary {
  id: string;
  jobDescription: string;
  status: "pending" | "approved" | "rejected";
  createdAt: string;
  recommendedExpertId?: string | null;
  reportDegraded: boolean;
  candidates: StaffingProposalCandidateSummary[];
  decidedByUserId?: string | null;
  decidedAt?: string | null;
  decisionNote?: string | null;
}

/** One stage's slice of the run: which agent identity acted (client id + scopes — provenance,
 * never credentials), what model it used, what it cost, when, and how it ended. */
export interface HandoffStageSlice {
  stage: string;
  agentClientId?: string | null;
  scopes: string[];
  modelId?: string | null;
  inputTokens: number;
  outputTokens: number;
  startedAt: string;
  completedAt: string;
  status: "completed" | "failed" | "skipped";
  degradeReason?: string | null;
  retryCount?: number | null;
}

/** The persisted handoff package (P1T-133): everything the approver needs to trust the run. */
export interface HandoffPackage {
  inputs: Record<string, string | null>;
  report: StaffingReport;
  provenance: {
    callerUserId?: string | null;
    capsSnapshotAtStart: { window: string; used: number; cap: number; resetAt: string }[];
    startedAt: string;
  };
  slices: HandoffStageSlice[];
  degradations: { stage: string; whatWasLost: string; why: string }[];
}

/** The drill-in: inbox metadata + the package (null on rows older than the package column). */
export interface StaffingProposalDetail extends StaffingProposalSummary {
  package?: HandoffPackage | null;
}

/** The approval inbox index. */
export function useStaffingProposals(status: string = "pending") {
  return useQuery({
    queryKey: ["staffing-proposals", status],
    queryFn: async () =>
      (await agentHttp.get<StaffingProposalSummary[]>(`/staffing/proposals?status=${status}`)).data,
  });
}

/** The approver drill-in (P1T-134): metadata + the full handoff package. */
export async function getStaffingProposal(id: string): Promise<StaffingProposalDetail> {
  const res = await agentHttp.get<StaffingProposalDetail>(`/staffing/proposals/${id}`);
  return res.data;
}
