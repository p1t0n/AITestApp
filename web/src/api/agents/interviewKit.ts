// Interview kit (P1T-102). Same input as Match/Tailoring; returns the markdown kit plus vetted
// structured questions. `evidence` is present only when the server verified the quote verbatim
// against the CV.
import { agentPost, type AgentJobRequest } from "./shared";

export interface InterviewQuestion {
  question: string;
  probes?: string | null;
  evidence?: string | null;
}

export interface InterviewKitResponse {
  answer: string;
  questions: InterviewQuestion[];
}

export const useInterviewKit = agentPost<AgentJobRequest, InterviewKitResponse>("/interview-kit");
