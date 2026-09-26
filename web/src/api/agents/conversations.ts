// Roster Q&A conversation history (EXP-33): the owner's own transcripts — list, read, delete one,
// delete all. No surface consumes these yet; the dock that will is EXP-34.
//
// Every route is scoped server-side to the caller's own account and none of them takes a user id,
// so there is nothing to pass here but a conversation id. A conversation that is not the caller's
// answers 404 exactly as a made-up id does — the client has no "forbidden" case to render, and
// should not invent one.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";

/** How a stored turn reads back. `removed` is the erasure scrub (permanent — an Expert the turn
 * named was erased); `hidden` is the pause mask (reversible, computed per request). Both arrive
 * with empty `question` and `answer`, and the state is what lets the dock say which. */
export type ConversationTurnState = "ok" | "removed" | "hidden";

export interface ConversationTurn {
  /** Empty unless `state` is `"ok"`. */
  question: string;
  /** Empty unless `state` is `"ok"`. Includes the "could not be grounded" note when there was
   * one, because it is exactly what was shown. */
  answer: string;
  /** The model the provider reported, or `""` when it named none. */
  modelId: string;
  /** Whether the answer rested on captured tool results — off the run, never parsed out of the
   * text. Readable even on a masked turn. */
  grounded: boolean;
  createdAt: string;
  state: ConversationTurnState;
}

/** One row of the history index. Titles and timestamps only — turn text is the drill-in's job. */
export interface ConversationSummary {
  id: string;
  /** The first question, trimmed at a word boundary. There is no rename and no model-written
   * title, so the only text here is text the owner typed. */
  title: string;
  createdAt: string;
  lastActiveAt: string;
  /** When retention deletes it: six calendar months past `lastActiveAt`. Served rather than
   * computed here, so the date the dock promises is the one the sweep acts on. */
  expiresAt: string;
}

export interface ConversationDetail {
  id: string;
  title: string;
  lastActiveAt: string;
  expiresAt: string;
  /** Oldest first, the way the transcript reads. */
  turns: ConversationTurn[];
}

const listKey = ["roster-qa-conversations"] as const;

/** The caller's conversations, most recently active first. */
export function useRosterQaConversations() {
  return useQuery({
    queryKey: listKey,
    queryFn: async () =>
      (await agentHttp.get<ConversationSummary[]>("/roster-qa/conversations")).data,
  });
}

/** One conversation and its turns. Disabled until there is an id to ask for, so opening the
 * drawer does not fetch a conversation nobody chose. */
export function useRosterQaConversation(id: string | null | undefined) {
  return useQuery({
    queryKey: [...listKey, id],
    queryFn: async () =>
      (await agentHttp.get<ConversationDetail>(`/roster-qa/conversations/${id}`)).data,
    enabled: Boolean(id),
  });
}

/** Deletes one conversation. Immediate and hard — the confirmation, where there is one, is the
 * dock's decision, not this layer's. */
export function useDeleteRosterQaConversation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await agentHttp.delete(`/roster-qa/conversations/${id}`);
      return id;
    },
    // The list is the only thing that can be stale afterwards; a detail query for a conversation
    // that is gone is not refetched, it is navigated away from.
    onSuccess: () => qc.invalidateQueries({ queryKey: listKey }),
  });
}

/** Deletes every conversation the caller owns. */
export function useDeleteAllRosterQaConversations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await agentHttp.delete("/roster-qa/conversations");
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: listKey }),
  });
}
