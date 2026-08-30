import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { selectAgentSurface } from "../test/agentSurface";
import type { AgentDock } from "./useAgentDock";
import type { AgentAnswer, JdMatchResponse } from "../api";

// ---- api module mock ----
// Only the hooks are mocked; apiErrorMessage stays real (mirrors the other widget test files).

const matchState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<AgentAnswer>>(),
  isPending: false,
};

const jdMatchState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<JdMatchResponse>>(),
  isPending: false,
};

const EMPLOYEE_ID = "11111111-2222-3333-4444-555555555555";
const ADA = "aaaaaaaa-1111-2222-3333-444444444444";
const GRACE = "bbbbbbbb-1111-2222-3333-444444444444";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useMatch: () => matchState,
    useJdMatch: () => jdMatchState,
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useApplyRewrite: () => ({ isPending: false, isSuccess: false, isError: false, error: null, mutate: vi.fn() }),
    useEmployees: () => ({
      data: [
        {
          id: EMPLOYEE_ID,
          firstName: "Ada",
          lastName: "Lovelace",
          title: "Senior Engineer",
          location: null,
          email: "ada@example.com",
          currentCapacityPercent: 100,
          status: "Active",
        },
      ],
      isLoading: false,
    }),
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

async function openMatchTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} isNarrow={false} />
    </MemoryRouter>,
  );
  await selectAgentSurface(user, "Match");
  return user;
}

beforeEach(() => {
  matchState.mutateAsync.mockReset();
  jdMatchState.mutateAsync.mockReset();
});

describe("Match tab — JD-only mode (P1T-103)", () => {
  it("runs JD-only mode when no employee is selected and renders ranked results", async () => {
    jdMatchState.mutateAsync.mockResolvedValue({
      requirements: ["kafka"],
      results: [
        {
          employeeId: ADA,
          name: "Ada Lovelace",
          title: "Platform Lead",
          retrievalScore: 0.95,
          status: "completed",
          score: 82,
          band: "Strong",
          answer: "## Analysis\n\nGreat fit.",
        },
        {
          employeeId: GRACE,
          name: "Grace Hopper",
          title: "Engineer",
          retrievalScore: 0.8,
          status: "failed",
          error: "model down",
        },
      ],
    });
    const user = await openMatchTab();

    await user.type(screen.getByPlaceholderText(/paste a job description/i), "Kafka engineer.");
    await user.click(screen.getByRole("button", { name: /find matches/i }));

    expect(jdMatchState.mutateAsync).toHaveBeenCalledWith({ jobDescription: "Kafka engineer." });
    expect(matchState.mutateAsync).not.toHaveBeenCalled();

    const top = await screen.findByTestId(`jd-match-${ADA}`);
    expect(top).toHaveTextContent("Ada Lovelace");
    expect(top).toHaveTextContent("82/100 · Strong");

    const failed = screen.getByTestId(`jd-match-${GRACE}`);
    expect(failed).toHaveTextContent("Match failed");
    expect(failed).toHaveTextContent("model down");

    // Analysis is collapsible.
    await user.click(screen.getByRole("button", { name: /show analysis/i }));
    expect(screen.getByText("Great fit.")).toBeInTheDocument();
  });

  it("keeps the single-employee path when an employee is selected", async () => {
    matchState.mutateAsync.mockResolvedValue({ answer: "Fit: MODERATE (60/100)" });
    const user = await openMatchTab();

    await user.click(screen.getByLabelText(/employee/i));
    await user.click(screen.getByText("Ada Lovelace — Senior Engineer"));
    await user.type(screen.getByPlaceholderText(/paste a job description/i), "Role.");
    await user.click(screen.getByRole("button", { name: /assess fit/i }));

    expect(matchState.mutateAsync).toHaveBeenCalledWith({
      employeeId: EMPLOYEE_ID,
      jobDescription: "Role.",
    });
    expect(jdMatchState.mutateAsync).not.toHaveBeenCalled();
    expect(await screen.findByText("Fit: MODERATE (60/100)")).toBeInTheDocument();
  });
});
