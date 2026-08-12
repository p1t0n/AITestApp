import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type { BenchReportResponse } from "../api";

const benchState = {
  mutateAsync: vi.fn<() => Promise<BenchReportResponse>>(),
  isPending: false,
};

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useBenchReport: () => benchState,
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useJdMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useApplyRewrite: () => ({ isPending: false, isSuccess: false, isError: false, error: null, mutate: vi.fn() }),
    useEmployees: () => ({ data: [], isLoading: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 420,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

async function openBenchTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} isNarrow={false} />
    </MemoryRouter>,
  );
  await user.click(screen.getByRole("tab", { name: "Bench" }));
  return user;
}

beforeEach(() => {
  benchState.mutateAsync.mockReset();
});

describe("Bench tab (P1T-104)", () => {
  it("renders server-composed stats, demand chips, and the narrative", async () => {
    benchState.mutateAsync.mockResolvedValue({
      answer: "## Narrative\n\nBench pressure is moderate.",
      stats: {
        activeEmployees: 12,
        fullyAvailable: 5,
        partiallyAvailable: 4,
        fullyBooked: 3,
        averageCapacityPercent: 61.5,
        topTitles: [{ name: "Engineer", count: 9 }],
        locations: [{ name: "London", count: 7 }],
        proposals: {
          total: 4,
          pending: 1,
          approved: 2,
          rejected: 1,
          recentJobDescriptions: ["Kafka platform engineer"],
          frequentCandidates: [{ name: "Ada Lovelace", count: 3 }],
        },
      },
      notes: [],
    });
    const user = await openBenchTab();

    await user.click(screen.getByRole("button", { name: /generate bench report/i }));

    const stats = await screen.findByTestId("bench-stats");
    expect(stats).toHaveTextContent("12");
    expect(stats).toHaveTextContent("61.5%");
    expect(screen.getByTestId("bench-proposals")).toHaveTextContent("4 runs — 1 pending, 2 approved, 1 rejected");
    expect(screen.getByText("Ada Lovelace ×3")).toBeInTheDocument();
    expect(screen.getByText("Bench pressure is moderate.")).toBeInTheDocument();
    expect(screen.queryByTestId("bench-notes")).toBeNull();
  });

  it("surfaces degrade notes when the report shipped partial", async () => {
    benchState.mutateAsync.mockResolvedValue({
      answer: "## Bench report (deterministic summary)\n\n- Active employees: 0",
      stats: {
        activeEmployees: 0,
        fullyAvailable: 0,
        partiallyAvailable: 0,
        fullyBooked: 0,
        averageCapacityPercent: 0,
        topTitles: [],
        locations: [],
        proposals: null,
      },
      notes: ["Roster stats unavailable (MCP server or auth failure)."],
    });
    const user = await openBenchTab();

    await user.click(screen.getByRole("button", { name: /generate bench report/i }));

    expect(await screen.findByTestId("bench-notes")).toHaveTextContent("Roster stats unavailable");
  });
});
