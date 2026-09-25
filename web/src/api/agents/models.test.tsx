import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { agentHttp, useAgentModels, type AgentModels } from "../index";

// Which model answers where (EXP-31). What is asserted here is the *fetch policy*, because that is
// the part the dock's caption cannot show you: this is a per-deployment fact, and re-asking for it
// on every mount would put a request behind every surface switch for an answer that cannot have
// changed. The rest of the hook's behaviour is in `components/AgentWidget.models.test.tsx`.

const MODELS: AgentModels = {
  provider: "Gemini",
  surfaces: { roster: ["gemini-3.5-flash-lite"] },
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

describe("useAgentModels", () => {
  it("reads the catalog from the agents backend", async () => {
    const get = vi.spyOn(agentHttp, "get").mockResolvedValue({ data: MODELS } as never);

    const { result } = renderHook(() => useAgentModels(), { wrapper });

    await waitFor(() => expect(result.current.data).toEqual(MODELS));
    expect(get).toHaveBeenCalledExactlyOnceWith("/models");
  });

  it("is fetched once a session, however many surfaces ask", async () => {
    const get = vi.spyOn(agentHttp, "get").mockResolvedValue({ data: MODELS } as never);

    const first = renderHook(() => useAgentModels(), { wrapper });
    await waitFor(() => expect(first.result.current.data).toEqual(MODELS));
    first.unmount();

    // A remount is what a dock close/open, or a second consumer, looks like. `staleTime: Infinity`
    // is what makes it free; the default zero would refetch here and on every window focus.
    const second = renderHook(() => useAgentModels(), { wrapper });
    await waitFor(() => expect(second.result.current.data).toEqual(MODELS));

    expect(get).toHaveBeenCalledTimes(1);
  });
});
