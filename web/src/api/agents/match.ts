// Match, in two modes against the same endpoint:
//   with an employeeId — one candidate, markdown prose ({ answer }).
//   without one (P1T-103) — shortlist retrieval picks the top candidates and the run fans out per
//   candidate. Failed entries degrade in place (status "failed" + error) rather than sinking the run.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";
import type { AgentAnswer, AgentJobRequest } from "./shared";

export function useMatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<AgentAnswer>("/match", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}

export interface JdMatchResult {
  employeeId: string;
  name: string;
  title: string;
  retrievalScore: number;
  status: "completed" | "failed";
  score?: number | null;
  band?: string | null;
  answer?: string | null;
  error?: string | null;
}

export interface JdMatchResponse {
  requirements: string[];
  results: JdMatchResult[];
}

export function useJdMatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: { jobDescription: string; topK?: number }) =>
      (await agentHttp.post<JdMatchResponse>("/match", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}
