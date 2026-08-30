import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { selectAgentSurface, currentAgentSurface } from "../test/agentSurface";
import type { AgentDock } from "./useAgentDock";
import type { RosterScanAccepted, RosterScanJob } from "../api";

// ---- api module mock ----

const submitState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<RosterScanAccepted>>(),
  isPending: false,
};

let jobState: { data: RosterScanJob | undefined } = { data: undefined };

const ADA = "11111111-2222-3333-4444-555555555555";
const GRACE = "99999999-8888-7777-6666-555555555555";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useSubmitRosterScan: () => submitState,
    useRosterScanJob: () => jobState,
    useSkills: () => ({ data: [], isLoading: false }),
    useEmployees: () => ({ data: [], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useJdMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 420,
  isNarrow: false,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

async function openScanTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
  await selectAgentSurface(user, "Roster scan");
  return user;
}

function job(overrides: Partial<RosterScanJob>): RosterScanJob {
  return {
    jobId: "job-1",
    state: "running",
    createdAt: new Date().toISOString(),
    jobDescription: "Kafka engineer",
    progress: { scored: 1, failed: 0, pending: 1, total: 2, settled: 1 },
    candidates: [
      {
        employeeId: ADA,
        name: "Ada Lovelace",
        title: "Engineer",
        status: "scored",
        score: 82,
        band: "Strong",
        rationale: "Deep Kafka evidence.",
        scorable: true,
      },
      { employeeId: GRACE, name: "Grace Hopper", title: "Admiral", status: "pending" },
    ],
    ...overrides,
  };
}

beforeEach(() => {
  submitState.mutateAsync = vi.fn();
  submitState.isPending = false;
  jobState = { data: undefined };
});

describe("Roster Scan tab", () => {
  it("submits and renders the honest estimate", async () => {
    submitState.mutateAsync.mockResolvedValue({
      jobId: "job-1",
      estimate: { candidates: 45, calls: 5, rpdBudget: 500 },
    });
    const user = await openScanTab();

    await user.type(screen.getByPlaceholderText(/scan the whole roster/i), "Kafka engineer");
    await user.click(screen.getByRole("button", { name: /scan the roster/i }));

    const estimate = await screen.findByTestId("scan-estimate");
    expect(estimate.textContent).toContain("45 candidate(s)");
    expect(estimate.textContent).toContain("5 model call(s)");
    expect(estimate.textContent).toContain("500/day budget");
  });

  it("shows progress and settled rows while pending rows stay hidden", async () => {
    jobState = { data: job({}) };
    await openScanTab();

    expect(screen.getByText("Scoring 1/2")).toBeInTheDocument();
    const row = screen.getByTestId(`scan-row-${ADA}`);
    expect(within(row).getByText("Strong · 82")).toBeInTheDocument();
    expect(screen.queryByTestId(`scan-row-${GRACE}`)).not.toBeInTheDocument();
  });

  it("renders the paused banner with reason and resume time", async () => {
    jobState = {
      data: job({ state: "paused", pauseReason: "quota", resumeAt: "2026-08-17T07:00:00Z" }),
    };
    await openScanTab();

    const banner = screen.getByTestId("scan-paused");
    expect(banner.textContent).toContain("model quota");
    expect(banner.textContent).toContain("Partial results below stay available");
  });

  it("renders a not-scorable chip for honest absence", async () => {
    jobState = {
      data: job({
        state: "completed",
        progress: { scored: 1, failed: 0, pending: 0, total: 1, settled: 1 },
        candidates: [
          {
            employeeId: ADA,
            name: "Ada Lovelace",
            title: "Engineer",
            status: "scored",
            score: null,
            band: null,
            scorable: false,
          },
        ],
      }),
    };
    await openScanTab();

    expect(screen.getByText("Not scorable")).toBeInTheDocument();
    expect(screen.getByText("Scan complete")).toBeInTheDocument();
  });

  it("'Open in Match' drills into the Match tab with the submitted JD", async () => {
    submitState.mutateAsync.mockResolvedValue({
      jobId: "job-1",
      estimate: { candidates: 1, calls: 1, rpdBudget: 500 },
    });
    // The poll already has data; submitting re-renders with the submitted JD captured.
    jobState = { data: job({}) };
    const user = await openScanTab();
    await user.type(screen.getByPlaceholderText(/scan the whole roster/i), "Kafka engineer JD");
    await user.click(screen.getByRole("button", { name: /scan the roster/i }));

    await user.click(within(await screen.findByTestId(`scan-row-${ADA}`)).getByRole("button", { name: /open in match/i }));

    expect(currentAgentSurface()).toBe("Match");
    expect(screen.getByDisplayValue("Kafka engineer JD")).toBeInTheDocument();
  });
});
