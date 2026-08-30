// Interview kit (P1T-102). Same input as Match/Tailoring; returns the markdown kit plus vetted
// structured questions. `evidence` is present only when the server verified the quote verbatim
// against the CV.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";
import type { AgentJobRequest } from "./shared";

export interface InterviewQuestion {
  question: string;
  probes?: string | null;
  evidence?: string | null;
}

export interface InterviewKitResponse {
  answer: string;
  questions: InterviewQuestion[];
}

export function useInterviewKit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<InterviewKitResponse>("/interview-kit", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}
