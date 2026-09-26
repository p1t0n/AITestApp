// The Roster Q&A surface's history hooks, stubbed (EXP-34).
//
// The surface is the dock's default pane, so *every* `AgentWidget.*` spec mounts it whether or not
// it is what that file is about — and all of them render outside a `QueryClientProvider`, which is
// what makes an unstubbed `useQuery` a thrown error rather than a pending one. This is that stub,
// in one place: a spec that does not care about history says so with one spread instead of four
// lines it would then have to keep true.
//
// The file that *is* about history (`AgentWidget.conversations.test.tsx`) writes its own, because
// what it needs from these hooks is the point rather than the scaffolding.
import type { ConversationDetail, ConversationSummary } from "../api";

interface Stubs {
  conversations?: ConversationSummary[];
  conversation?: ConversationDetail;
}

const idleMutation = () => ({
  mutate: () => {},
  mutateAsync: async () => {},
  isPending: false,
  isError: false,
  error: null,
});

/** Spread into a `vi.mock("../api", …)` factory, after `...actual`. */
export function rosterQaHistoryMocks({ conversations = [], conversation }: Stubs = {}) {
  return {
    useRosterQaConversations: () => ({ data: conversations, isLoading: false }),
    useRosterQaConversation: () => ({ data: conversation, isLoading: false }),
    useDeleteRosterQaConversation: idleMutation,
    useDeleteAllRosterQaConversations: idleMutation,
  };
}
