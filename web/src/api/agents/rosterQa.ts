// Roster Q&A: the one threaded, read-only agent.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface RosterQaResponse {
  answer: string;
  /** The conversation to continue. A returned id differing from the one sent means the server
   * started a fresh thread (expired/unknown) — the prior context is gone. */
  threadId: string;
}

export interface RosterQaInput {
  question: string;
  threadId?: string;
}

/** Ask the Roster Q&A agent. Pass the last response's threadId to continue the conversation. */
export function useRosterQa() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RosterQaInput) =>
      (await agentHttp.post<RosterQaResponse>("/roster-qa", input)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}
