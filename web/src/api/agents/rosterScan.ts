// Roster Scan (P1T-125): exhaustive async scoring of the (filtered) roster against one JD. Submit
// returns 202 with a job to poll — no SSE, because jobs span hours and quota pauses, and the job
// keeps running when the widget closes. Pausing (quota/cap windows) is the normal path, not an error.
import { useMutation, useQuery } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface RosterScanRequest {
  jobDescription: string;
  availableOn?: string;
  skillIds?: string[];
  location?: string;
  minYears?: number;
}

export interface RosterScanEstimate {
  candidates: number;
  calls: number;
  rpdBudget: number;
}

export interface RosterScanAccepted {
  jobId: string;
  estimate: RosterScanEstimate;
}

export type RosterScanState = "queued" | "running" | "paused" | "completed" | "failed";
export type RosterScanCandidateStatus = "pending" | "scored" | "failed";

export interface RosterScanCandidate {
  expertId: string;
  name: string;
  title: string;
  status: RosterScanCandidateStatus;
  score?: number | null;
  band?: string | null;
  rationale?: string | null;
  /** False with null score/band = the honest "the digest gave nothing to judge" outcome. */
  scorable?: boolean | null;
  error?: string | null;
}

export interface RosterScanProgress {
  scored: number;
  failed: number;
  pending: number;
  total: number;
  settled: number;
}

export interface RosterScanJob {
  jobId: string;
  state: RosterScanState;
  pauseReason?: "quota" | "cap" | null;
  resumeAt?: string | null;
  failureDetail?: string | null;
  createdAt: string;
  jobDescription: string;
  progress: RosterScanProgress;
  candidates: RosterScanCandidate[];
}

export function useSubmitRosterScan() {
  return useMutation({
    mutationFn: async (req: RosterScanRequest) =>
      (await agentHttp.post<RosterScanAccepted>("/roster-scan", req)).data,
  });
}

/** Polls one job while it is live; settles to no-refetch once terminal. */
export function useRosterScanJob(jobId: string | null) {
  return useQuery({
    queryKey: ["roster-scan", jobId],
    enabled: jobId !== null,
    queryFn: async () => (await agentHttp.get<RosterScanJob>(`/roster-scan/${jobId}`)).data,
    refetchInterval: (query) => {
      const state = query.state.data?.state;
      return state === "completed" || state === "failed" ? false : 3000;
    },
  });
}

