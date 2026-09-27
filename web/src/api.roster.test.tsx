import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { http, useExperts, useRoster } from "./api";
import type { ExpertSummary } from "./types";

/**
 * Two lists, one endpoint (EXP-49). `useExperts` is the **bench** — published people, which is
 * what the agent pickers may offer. `useRoster` is the whole **Roster**, Drafts included, which
 * is what the staff roster page and the ⌘K palette show. Widening the shared hook would have put
 * unvetted Drafts into the pickers, so the difference is asserted at the request itself.
 */

const BENCH: ExpertSummary[] = [
  {
    id: "a",
    firstName: "Published",
    lastName: "Activeson",
    title: "Engineer",
    location: null,
    email: "a@example.com",
    currentCapacityPercent: 100,
    status: "Active",
  },
];

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

describe("the two roster reads", () => {
  it("useExperts asks for the bench — no drafts on the request", async () => {
    const get = vi.spyOn(http, "get").mockResolvedValue({ data: BENCH } as never);

    const { result } = renderHook(() => useExperts(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(get).toHaveBeenCalledWith("/experts");
  });

  it("useRoster asks for the whole Roster, Drafts included", async () => {
    const get = vi.spyOn(http, "get").mockResolvedValue({ data: BENCH } as never);

    const { result } = renderHook(() => useRoster(), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(get).toHaveBeenCalledWith("/experts", { params: { includeDrafts: true } });
  });

  it("caches the two lists apart, both under the ['experts'] prefix so invalidation still reaches them", async () => {
    vi.spyOn(http, "get").mockResolvedValue({ data: BENCH } as never);

    const bench = renderHook(() => useExperts(), { wrapper });
    const roster = renderHook(() => useRoster(), { wrapper });
    await waitFor(() => expect(bench.result.current.isSuccess).toBe(true));
    await waitFor(() => expect(roster.result.current.isSuccess).toBe(true));

    const keys = queryClient.getQueryCache().getAll().map((q) => q.queryKey);
    expect(keys).toContainEqual(["experts"]);
    expect(keys).toContainEqual(["experts", "roster"]);

    // What `invalidateQueries({ queryKey: ["experts"] })` — the mutations' existing call — reaches.
    expect(
      queryClient.getQueryCache().findAll({ queryKey: ["experts"] }).map((q) => q.queryKey),
    ).toHaveLength(2);
  });
});
