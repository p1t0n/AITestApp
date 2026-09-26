import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  agentHttp,
  useDeleteAllRosterQaConversations,
  useDeleteRosterQaConversation,
  useRosterQa,
  useRosterQaConversation,
  useRosterQaConversations,
  type ConversationDetail,
  type ConversationSummary,
} from "../index";

// The history client (EXP-33). Two things are asserted here that the types alone cannot hold: the
// URLs (these go to the *agents* backend, not the Web API, and the SPA has no other way to say so)
// and the invalidation — a delete whose list is not invalidated leaves a row on screen that the
// server no longer has, which is the one failure mode a user would read as "it didn't work".

const SUMMARY: ConversationSummary = {
  id: "5b0f0e3f-1c8a-4c3e-9a2b-000000000001",
  title: "Who knows React?",
  createdAt: "2026-01-02T08:00:00+00:00",
  lastActiveAt: "2026-01-15T09:30:00+00:00",
  expiresAt: "2026-07-15T09:30:00+00:00",
};

const DETAIL: ConversationDetail = {
  id: SUMMARY.id,
  title: SUMMARY.title,
  lastActiveAt: SUMMARY.lastActiveAt,
  expiresAt: SUMMARY.expiresAt,
  turns: [
    {
      question: "Who knows React?",
      answer: "Ada Lovelace does.",
      modelId: "gemini-3.5-flash-lite",
      grounded: true,
      createdAt: "2026-01-15T09:30:00+00:00",
      state: "ok",
    },
    {
      question: "",
      answer: "",
      modelId: "gemini-3.5-flash-lite",
      grounded: true,
      createdAt: "2026-01-15T09:31:00+00:00",
      state: "hidden",
    },
  ],
};

let queryClient: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

describe("useRosterQaConversations", () => {
  it("reads the caller's index from the agents backend", async () => {
    const get = vi.spyOn(agentHttp, "get").mockResolvedValue({ data: [SUMMARY] } as never);

    const { result } = renderHook(() => useRosterQaConversations(), { wrapper });

    await waitFor(() => expect(result.current.data).toEqual([SUMMARY]));
    // No user id in the URL: the route is scoped to the caller's own account server-side, and a
    // client that could name an owner would be a client that could ask for someone else's.
    expect(get).toHaveBeenCalledExactlyOnceWith("/roster-qa/conversations");
  });
});

describe("useRosterQaConversation", () => {
  it("reads one conversation, masked turns and all", async () => {
    const get = vi.spyOn(agentHttp, "get").mockResolvedValue({ data: DETAIL } as never);

    const { result } = renderHook(() => useRosterQaConversation(SUMMARY.id), { wrapper });

    await waitFor(() => expect(result.current.data).toEqual(DETAIL));
    expect(get).toHaveBeenCalledExactlyOnceWith(`/roster-qa/conversations/${SUMMARY.id}`);
    expect(result.current.data?.turns[1].state).toBe("hidden");
  });

  it("asks for nothing until a conversation is chosen", () => {
    const get = vi.spyOn(agentHttp, "get").mockResolvedValue({ data: DETAIL } as never);

    renderHook(() => useRosterQaConversation(null), { wrapper });

    expect(get).not.toHaveBeenCalled();
  });
});

describe("deleting", () => {
  it("removes one conversation and refetches the list", async () => {
    const del = vi.spyOn(agentHttp, "delete").mockResolvedValue({ status: 204 } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useDeleteRosterQaConversation(), { wrapper });
    result.current.mutate(SUMMARY.id);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(del).toHaveBeenCalledExactlyOnceWith(`/roster-qa/conversations/${SUMMARY.id}`);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["roster-qa-conversations"] });
  });

  it("removes all of them and refetches the list", async () => {
    const del = vi.spyOn(agentHttp, "delete").mockResolvedValue({ status: 204 } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useDeleteAllRosterQaConversations(), { wrapper });
    result.current.mutate();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(del).toHaveBeenCalledExactlyOnceWith("/roster-qa/conversations");
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["roster-qa-conversations"] });
  });
});

describe("asking a question", () => {
  it("refetches the index, because the answer moved this conversation to the top of it", async () => {
    vi.spyOn(agentHttp, "post").mockResolvedValue({
      data: { answer: "Ada Lovelace does.", threadId: SUMMARY.id, modelId: "m" },
    } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useRosterQa(), { wrapper });
    result.current.mutate({ question: "Who knows React?" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    // `exact`, and the word is the assertion: the detail query is keyed under the same prefix, so
    // a non-exact invalidation refetches the transcript the dock has just appended this answer to
    // and the answer renders twice. The delete paths above invalidate the prefix on purpose.
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: ["roster-qa-conversations"],
      exact: true,
    });
    // …and the ledger it always invalidated, which a second key must not have displaced.
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["usage"] });
  });
});
