import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { http, useApplyRewrite, type ApplyRewriteInput } from "./api";

// The design rule (P1T-62) says the agent never writes: applying a tailoring rewrite is a plain
// Web-API edit with the user's own session. Since P1T-90 that edit is one PATCH per bullet —
// no read-modify-write of the whole experience, no regenerated sibling ids, no lost-update race.

const EXPERT_ID = "11111111-2222-3333-4444-555555555555";
const EXPERIENCE_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const ACHIEVEMENT_1 = "a1a1a1a1-1111-1111-1111-111111111111";

const INPUT: ApplyRewriteInput = {
  expertId: EXPERT_ID,
  experienceId: EXPERIENCE_ID,
  achievementId: ACHIEVEMENT_1,
  original: "Worked on React apps",
  rewritten: "Shipped production React apps used by 40k people",
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
});

describe("useApplyRewrite", () => {
  it("PATCHes the one bullet with the rewritten text and nothing else", async () => {
    const patch = vi.spyOn(http, "patch").mockResolvedValue({
      data: { id: ACHIEVEMENT_1, order: 1, text: INPUT.rewritten },
    } as never);
    const get = vi.spyOn(http, "get");
    const put = vi.spyOn(http, "put");

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    result.current.mutate(INPUT);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(patch).toHaveBeenCalledWith(`/achievements/${ACHIEVEMENT_1}`, {
      text: INPUT.rewritten,
    });
    expect(get).not.toHaveBeenCalled();
    expect(put).not.toHaveBeenCalled();
  });

  it("invalidates the expert queries so open detail/CV views refetch", async () => {
    vi.spyOn(http, "patch").mockResolvedValue({
      data: { id: ACHIEVEMENT_1, order: 1, text: INPUT.rewritten },
    } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    result.current.mutate(INPUT);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["experts", EXPERT_ID] });
  });

  it("surfaces a failed PATCH as a mutation error", async () => {
    vi.spyOn(http, "patch").mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    result.current.mutate(INPUT);

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
