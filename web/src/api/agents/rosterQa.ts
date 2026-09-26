// Roster Q&A: the one threaded, read-only agent.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface RosterQaResponse {
  answer: string;
  /** The conversation to continue. A returned id differing from the one sent means the server
   * started a fresh thread (expired/unknown) — the prior context is gone. */
  threadId: string;
  /** The model the provider reported for this answer (EXP-31) — what actually wrote it, which can
   * differ from the model `useAgentModels` names as configured when an alias resolves. Null when
   * the provider named none; the transcript then shows no caption rather than a guess. */
  modelId?: string | null;
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
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["usage"] });
      // An answer either created a conversation or moved one to the top of the index, and either
      // way the history drawer is now wrong (EXP-34). It is also where the dock reads the current
      // conversation's title from, so without this a brand-new thread stays called "New
      // conversation" until something else happened to refetch.
      //
      // `exact`, and this is load-bearing rather than tidy: a conversation's *detail* query is
      // keyed `["roster-qa-conversations", id]`, so a prefix invalidation refetches the open
      // transcript as well — which now contains the turn the dock has just appended locally, and
      // the answer renders twice. A delete still cascades to the detail on purpose (that
      // conversation really is gone); an answer must not, because the client is already holding
      // the only thing that changed. Caught by `e2e/roster-qa-history.e2e.ts` in a real browser;
      // a jsdom spec with these hooks mocked cannot see it at all.
      qc.invalidateQueries({ queryKey: ["roster-qa-conversations"], exact: true });
    },
  });
}
