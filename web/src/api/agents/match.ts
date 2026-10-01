// Match, in two modes against the same endpoint:
//   with an expertId — one candidate, markdown prose ({ answer }).
//   without one (P1T-103) — shortlist retrieval picks the top candidates and the run fans out per
//   candidate. Failed entries degrade in place (status "failed" + error) rather than sinking the run.
import { agentPost, type AgentAnswer, type AgentJobRequest } from "./shared";

export const useMatch = agentPost<AgentJobRequest, AgentAnswer>("/match");

export interface JdMatchResult {
  expertId: string;
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

export const useJdMatch = agentPost<{ jobDescription: string; topK?: number }, JdMatchResponse>(
  "/match",
);
