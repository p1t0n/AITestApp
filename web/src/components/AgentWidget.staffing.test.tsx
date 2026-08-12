import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type {
  StaffingReport,
  StaffingRequest,
  StaffingRunHandlers,
} from "../api";

// ---- api module mock ----
// Only the hooks + runStaffing are mocked; apiErrorMessage stays real so error shapes go through
// the same extraction path production uses (mirrors AgentWidget.shortlist.test.tsx). runStaffing
// records every call so tests can drive scripted SSE event sequences through the handlers.

interface StaffingCall {
  req: StaffingRequest;
  handlers: StaffingRunHandlers;
  signal: AbortSignal | undefined;
}

const staffing = {
  calls: [] as StaffingCall[],
  impl: (() => Promise.resolve()) as (
    req: StaffingRequest,
    handlers: StaffingRunHandlers,
    signal?: AbortSignal,
  ) => Promise<void>,
};

const decisions = {
  calls: [] as { proposalId: string; decision: string; note?: string }[],
  impl: ((proposalId: string, decision: string) =>
    Promise.resolve({ id: proposalId, status: decision })) as (
    proposalId: string,
    decision: "approved" | "rejected",
    note?: string,
  ) => Promise<{ id: string; status: string; decisionNote?: string | null }>,
};

const ADA = "11111111-2222-3333-4444-555555555555";
const GRACE = "66666666-7777-8888-9999-aaaaaaaaaaaa";
const LIN = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    runStaffing: (req: StaffingRequest, handlers: StaffingRunHandlers, signal?: AbortSignal) => {
      staffing.calls.push({ req, handlers, signal });
      return staffing.impl(req, handlers, signal);
    },
    decideStaffingProposal: (proposalId: string, decision: "approved" | "rejected", note?: string) => {
      decisions.calls.push({ proposalId, decision, note });
      return decisions.impl(proposalId, decision, note);
    },
    useSkills: () => ({
      data: [
        { id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", name: "React", categoryId: "c1", categoryName: "Frontend", rank: 1 },
      ],
      isLoading: false,
    }),
    useEmployees: () => ({
      data: [
        {
          id: ADA,
          firstName: "Ada",
          lastName: "Lovelace",
          title: "Senior Engineer",
          location: null,
          email: "ada@example.com",
          currentCapacityPercent: 100,
  status: "Active",
        },
        {
          id: GRACE,
          firstName: "Grace",
          lastName: "Hopper",
          title: "Compiler Engineer",
          location: null,
          email: "grace@example.com",
          currentCapacityPercent: 100,
  status: "Active",
        },
      ],
      isLoading: false,
    }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const REPORT: StaffingReport = {
  requirements: ["React expertise", "Team leadership"],
  candidates: [
    {
      employeeId: ADA,
      name: "Ada Lovelace",
      title: "Senior Engineer",
      shortlist: {
        score: 0.9234,
        coverage: { matched: 2, total: 2 },
        requirements: [
          { text: "React expertise", matched: true, snippet: "Built React apps for 6 years" },
          { text: "Team leadership", matched: true, snippet: "Led a team of 5" },
        ],
      },
      match: {
        status: "completed",
        score: 78,
        band: "Strong",
        answer: "## Fit assessment\n\nAda is a **great** fit.",
        error: null,
      },
      rationale: "Ada leads on coverage and depth.",
    },
    {
      employeeId: GRACE,
      name: "Grace Hopper",
      title: "Compiler Engineer",
      shortlist: {
        score: 0.81,
        coverage: { matched: 1, total: 2 },
        requirements: [
          { text: "React expertise", matched: false },
          { text: "Team leadership", matched: true, snippet: "Ran the compiler group" },
        ],
      },
      match: { status: "failed", score: null, band: null, answer: null, error: "model timeout" },
      rationale: "Strong systems background, weaker frontend evidence.",
    },
    {
      employeeId: LIN,
      name: "Lin Zhou",
      title: "Frontend Engineer",
      shortlist: {
        score: 0.74,
        coverage: { matched: 1, total: 2 },
        requirements: [
          { text: "React expertise", matched: true, snippet: "Shipped React dashboards" },
          { text: "Team leadership", matched: false },
        ],
      },
      match: { status: "skipped", score: null, band: null, answer: null, error: null },
      rationale: "Solid React skills; match run skipped.",
    },
  ],
  recommendation: { employeeId: ADA, narrative: "Ada is the strongest fit overall." },
  degraded: false,
  notes: [],
};

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 420,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

async function openStaffingTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} isNarrow={false} />
    </MemoryRouter>,
  );
  await user.click(screen.getByRole("tab", { name: "Staffing" }));
  return user;
}

function jdField() {
  return screen.getByPlaceholderText(/paste a job description/i);
}

function submitButton() {
  return screen.getByRole("button", { name: /run staffing/i });
}

/** Submits a run whose runStaffing promise never settles, so tests can feed events by hand. */
async function startHangingRun(user: Awaited<ReturnType<typeof openStaffingTab>>) {
  staffing.impl = () => new Promise<void>(() => {});
  await user.type(jdField(), "Senior React engineer");
  await user.click(submitButton());
  return staffing.calls[staffing.calls.length - 1];
}

/** Submits a run that immediately delivers the given report and resolves. */
async function runToReport(user: Awaited<ReturnType<typeof openStaffingTab>>, report: StaffingReport) {
  staffing.impl = async (_req, handlers) => {
    handlers.onStep({ stage: "shortlist", status: "started" });
    handlers.onStep({ stage: "shortlist", status: "completed" });
    handlers.onReport(report);
  };
  await user.type(jdField(), "Senior React engineer");
  await user.click(submitButton());
  await screen.findByTestId("staffing-recommendation");
}

function candidateCard(employeeId: string) {
  return screen.getByTestId(`staffing-candidate-${employeeId}`);
}

beforeEach(() => {
  staffing.calls = [];
  staffing.impl = () => Promise.resolve();
  decisions.calls = [];
  decisions.impl = (proposalId, decision) => Promise.resolve({ id: proposalId, status: decision });
});

describe("staffing proposal approval (P1T-100)", () => {
  const PROPOSAL_ID = "dddddddd-1111-2222-3333-444444444444";

  it("offers approve/reject when the report carries a proposal id and records the decision", async () => {
    const user = await openStaffingTab();
    await runToReport(user, { ...REPORT, proposalId: PROPOSAL_ID });

    const card = screen.getByTestId("proposal-decision");
    await user.click(within(card).getByRole("button", { name: "Approve" }));

    expect(decisions.calls).toEqual([{ proposalId: PROPOSAL_ID, decision: "approved", note: undefined }]);
    expect(await screen.findByTestId("proposal-decided")).toHaveTextContent(/approved/i);
  });

  it("renders no decision card when the report has no proposal id", async () => {
    const user = await openStaffingTab();
    await runToReport(user, REPORT);

    expect(screen.queryByTestId("proposal-decision")).toBeNull();
  });

  it("keeps the buttons live for retry when the decision call fails", async () => {
    decisions.impl = () => Promise.reject(new Error("boom"));
    const user = await openStaffingTab();
    await runToReport(user, { ...REPORT, proposalId: PROPOSAL_ID });

    const card = screen.getByTestId("proposal-decision");
    await user.click(within(card).getByRole("button", { name: "Reject" }));

    expect(await screen.findByText("boom")).toBeInTheDocument();
    expect(within(screen.getByTestId("proposal-decision")).getByRole("button", { name: "Reject" }))
      .toBeEnabled();
  });
});

describe("Staffing tab — inputs", () => {
  it("renders the JD input, presets, collapsed filters, and the match-top selector", async () => {
    const user = await openStaffingTab();

    expect(jdField()).toBeInTheDocument();
    expect(screen.getByText("Senior React Engineer")).toBeInTheDocument(); // preset chip
    expect(screen.getByLabelText("Candidates to match")).toHaveTextContent("3"); // default
    expect(screen.queryByLabelText("Available on")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /filters/i }));

    expect(screen.getByLabelText("Available on")).toBeInTheDocument();
    expect(screen.getByLabelText("Skills")).toBeInTheDocument();
    expect(screen.getByLabelText("Location")).toBeInTheDocument();
    expect(screen.getByLabelText("Min years")).toBeInTheDocument();
  });

  it("disables submit while the JD is blank and while a run is in flight", async () => {
    const user = await openStaffingTab();

    expect(submitButton()).toBeDisabled();
    await user.type(jdField(), "Senior React engineer");
    expect(submitButton()).toBeEnabled();

    staffing.impl = () => new Promise<void>(() => {});
    await user.click(submitButton());
    expect(screen.getByRole("button", { name: /running/i })).toBeDisabled();
  });

  it("submits only the JD and the default matchTop when no filters are set", async () => {
    const user = await openStaffingTab();

    await user.type(jdField(), "  Senior React engineer  ");
    await user.click(submitButton());

    expect(staffing.calls[staffing.calls.length - 1].req).toEqual({
      jobDescription: "Senior React engineer",
      matchTop: 3,
    });
  });

  it("serializes the user-set filters and the chosen matchTop", async () => {
    const user = await openStaffingTab();

    await user.type(jdField(), "Senior React engineer");
    await user.click(screen.getByRole("button", { name: /filters/i }));

    fireEvent.change(screen.getByLabelText("Available on"), { target: { value: "2026-08-01" } });
    await user.click(screen.getByLabelText("Skills"));
    await user.click(await screen.findByRole("option", { name: "React" }));
    await user.type(screen.getByLabelText("Location"), "Berlin");
    await user.type(screen.getByLabelText("Min years"), "5");
    await user.click(screen.getByLabelText("Candidates to match"));
    await user.click(await screen.findByRole("option", { name: "5" }));

    await user.click(submitButton());

    expect(staffing.calls[staffing.calls.length - 1].req).toEqual({
      jobDescription: "Senior React engineer",
      availableOn: "2026-08-01",
      skillIds: ["aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"],
      location: "Berlin",
      minYears: 5,
      matchTop: 5,
    });
  });
});

describe("Staffing tab — progress stepper", () => {
  it("follows a scripted run: stages activate, matches tick per candidate with k/N", async () => {
    const user = await openStaffingTab();
    const { handlers } = await startHangingRun(user);

    const shortlistRow = () => screen.getByTestId("staffing-step-shortlist");
    const matchRow = () => screen.getByTestId("staffing-step-match");
    const narrativeRow = () => screen.getByTestId("staffing-step-narrative");

    act(() => handlers.onStep({ stage: "shortlist", status: "started" }));
    expect(within(shortlistRow()).getByRole("progressbar")).toBeInTheDocument();

    act(() => handlers.onStep({ stage: "shortlist", status: "completed" }));
    expect(within(shortlistRow()).getByTestId("CheckCircleOutlineIcon")).toBeInTheDocument();

    act(() =>
      handlers.onStep({
        stage: "match",
        status: "started",
        candidate: { employeeId: ADA, name: "Ada Lovelace" },
        totalCount: 2,
      }),
    );
    expect(within(matchRow()).getByRole("progressbar")).toBeInTheDocument();
    expect(within(matchRow()).getByText("Matching (0/2)")).toBeInTheDocument();

    act(() =>
      handlers.onStep({
        stage: "match",
        status: "completed",
        candidate: { employeeId: ADA, name: "Ada Lovelace" },
        completedCount: 1,
        totalCount: 2,
      }),
    );
    expect(within(matchRow()).getByText("Matching (1/2)")).toBeInTheDocument();
    expect(within(matchRow()).getByText("Ada Lovelace")).toBeInTheDocument();

    act(() =>
      handlers.onStep({
        stage: "match",
        status: "failed",
        candidate: { employeeId: GRACE, name: "Grace Hopper" },
        completedCount: 2,
        totalCount: 2,
        error: "model timeout",
      }),
    );
    expect(within(matchRow()).getByText("Matching (2/2)")).toBeInTheDocument();
    const graceTick = within(matchRow()).getByText("Grace Hopper").closest("[data-testid=staffing-match-tick]")!;
    expect(within(graceTick as HTMLElement).getByTestId("WarningAmberIcon")).toBeInTheDocument();
    expect(within(matchRow()).getByTestId("CheckCircleOutlineIcon")).toBeInTheDocument(); // 2/2 → done

    act(() => handlers.onStep({ stage: "narrative", status: "started" }));
    expect(within(narrativeRow()).getByRole("progressbar")).toBeInTheDocument();

    act(() => handlers.onStep({ stage: "narrative", status: "completed" }));
    expect(within(narrativeRow()).getByTestId("CheckCircleOutlineIcon")).toBeInTheDocument();

    act(() => handlers.onReport(REPORT));
    expect(within(screen.getByTestId("staffing-step-done")).getByTestId("CheckCircleOutlineIcon")).toBeInTheDocument();
  });

  it("shows an inline warning when the narrative step fails but the run continues", async () => {
    const user = await openStaffingTab();
    const { handlers } = await startHangingRun(user);

    act(() => handlers.onStep({ stage: "shortlist", status: "started" }));
    act(() => handlers.onStep({ stage: "shortlist", status: "completed" }));
    act(() =>
      handlers.onStep({ stage: "narrative", status: "failed", error: "narrative model fault" }),
    );

    const narrativeRow = screen.getByTestId("staffing-step-narrative");
    expect(within(narrativeRow).getByTestId("WarningAmberIcon")).toBeInTheDocument();
    expect(within(narrativeRow).getByText("narrative model fault")).toBeInTheDocument();

    // The run still finishes into a (degraded) report.
    act(() => handlers.onReport({ ...REPORT, recommendation: null, degraded: true, notes: ["x"] }));
    expect(screen.getByTestId("staffing-recommendation")).toBeInTheDocument();
  });
});

describe("Staffing tab — report", () => {
  it("renders the recommendation first, then requirements and ranked candidate cards", async () => {
    const user = await openStaffingTab();
    await runToReport(user, REPORT);

    const recommendation = screen.getByTestId("staffing-recommendation");
    const recLink = within(recommendation).getByRole("link", { name: "Ada Lovelace" });
    expect(recLink).toHaveAttribute("href", `/employees/${ADA}`);
    expect(
      within(recommendation).getByText("Ada is the strongest fit overall."),
    ).toBeInTheDocument();

    // Recommendation-first: the block precedes every candidate card in the DOM.
    const firstCard = candidateCard(ADA);
    expect(
      recommendation.compareDocumentPosition(firstCard) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    expect(screen.getByText("How the JD was read")).toBeInTheDocument();
    expect(screen.getByText("Team leadership")).toBeInTheDocument();

    const ada = candidateCard(ADA);
    expect(within(ada).getByRole("link", { name: "Ada Lovelace" })).toHaveAttribute(
      "href",
      `/employees/${ADA}`,
    );
    expect(within(ada).getByText("Senior Engineer")).toBeInTheDocument();
    expect(within(ada).getByText("0.92")).toBeInTheDocument();
    expect(within(ada).getByText("2/2")).toBeInTheDocument();
    expect(within(ada).getByText("Strong · 78")).toBeInTheDocument();
    expect(within(ada).getByText("Ada leads on coverage and depth.")).toBeInTheDocument();
  });

  it("shows a muted note instead when the report carries no recommendation", async () => {
    const user = await openStaffingTab();
    await runToReport(user, { ...REPORT, recommendation: null, degraded: true, notes: ["The narrative step failed."] });

    expect(
      within(screen.getByTestId("staffing-recommendation")).getByText(/no recommendation/i),
    ).toBeInTheDocument();
  });

  it("hides the band/score chip when the match completed without a readable score", async () => {
    const unreadable = {
      ...REPORT,
      candidates: [
        {
          ...REPORT.candidates[0],
          match: { status: "completed" as const, score: null, band: null, answer: "Some markdown", error: null },
        },
      ],
    };
    const user = await openStaffingTab();
    await runToReport(user, unreadable);

    expect(within(candidateCard(ADA)).queryByTestId("staffing-band-chip")).not.toBeInTheDocument();
  });

  it("renders the degraded banner with notes, and failed/skipped match chips", async () => {
    const user = await openStaffingTab();
    await runToReport(user, {
      ...REPORT,
      degraded: true,
      notes: ["Match failed for Grace Hopper.", "Narrative degraded."],
    });

    const banner = screen.getByTestId("staffing-degraded");
    expect(within(banner).getByText("Match failed for Grace Hopper.")).toBeInTheDocument();
    expect(within(banner).getByText("Narrative degraded.")).toBeInTheDocument();

    const grace = candidateCard(GRACE);
    expect(within(grace).getByText("Match failed")).toBeInTheDocument();
    expect(within(grace).queryByRole("button", { name: /match details/i })).not.toBeInTheDocument();

    const lin = candidateCard(LIN);
    expect(within(lin).getByText("Match skipped")).toBeInTheDocument();
    expect(within(lin).queryByRole("button", { name: /match details/i })).not.toBeInTheDocument();
  });

  it("expands the verbatim match markdown and the shortlist evidence", async () => {
    const user = await openStaffingTab();
    await runToReport(user, REPORT);

    const ada = candidateCard(ADA);
    await user.click(within(ada).getByRole("button", { name: /match details/i }));
    expect(await within(ada).findByRole("heading", { name: "Fit assessment" })).toBeInTheDocument();

    await user.click(within(ada).getByRole("button", { name: /evidence/i }));
    expect(within(ada).getByText("Built React apps for 6 years")).toBeInTheDocument();
  });

  it("'Open in Match' switches to the Match tab with the employee and JD pre-filled", async () => {
    const user = await openStaffingTab();
    await runToReport(user, REPORT);

    await user.click(within(candidateCard(GRACE)).getByRole("button", { name: /open in match/i }));

    expect(screen.getByRole("tab", { name: "Match" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByDisplayValue("Grace Hopper — Compiler Engineer")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Senior React engineer")).toBeInTheDocument();
  });

  it("'Tailor CV' switches to the Tailor CV tab with the employee and JD pre-filled", async () => {
    const user = await openStaffingTab();
    await runToReport(user, REPORT);

    await user.click(within(candidateCard(ADA)).getByRole("button", { name: "Tailor CV" }));

    expect(screen.getByRole("tab", { name: "Tailor CV" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByDisplayValue("Ada Lovelace — Senior Engineer")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Senior React engineer")).toBeInTheDocument();
  });
});

describe("Staffing tab — errors and edge cases", () => {
  it("renders the terminal error event through the shared error presentation", async () => {
    staffing.impl = async (_req, handlers) => {
      handlers.onStep({ stage: "shortlist", status: "started" });
      handlers.onError({
        title: "Upstream dependency failed (staffing shortlist step).",
        detail: "The retrieval index is unavailable.",
      });
    };
    const user = await openStaffingTab();
    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());

    expect(
      await screen.findByText(/upstream dependency failed \(staffing shortlist step\)\./i),
    ).toBeInTheDocument();
    expect(screen.getByText(/the retrieval index is unavailable\./i)).toBeInTheDocument();
    expect(submitButton()).toBeEnabled();
  });

  it("renders the structured 429 cap message the same way other tabs do", async () => {
    // The SSE helper turns the pre-stream 429 into an Error whose message is the cap body's
    // `error` field (see sse.test.ts); the panel surfaces it via apiErrorMessage.
    staffing.impl = () =>
      Promise.reject(
        Object.assign(new Error("Your daily token cap has been reached."), {
          name: "SseHttpError",
          status: 429,
        }),
      );
    const user = await openStaffingTab();
    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());

    expect(await screen.findByText("Your daily token cap has been reached.")).toBeInTheDocument();
  });

  it("marks a dropped stream as failed but stays re-submittable", async () => {
    staffing.impl = () => Promise.reject(new Error("The network connection was lost."));
    const user = await openStaffingTab();
    await user.type(jdField(), "Senior React engineer");
    await user.click(submitButton());

    expect(await screen.findByText("The network connection was lost.")).toBeInTheDocument();
    expect(submitButton()).toBeEnabled();

    // A resubmit clears the failed state and can complete normally.
    staffing.impl = async (_req, handlers) => handlers.onReport(REPORT);
    await user.click(submitButton());
    expect(await screen.findByTestId("staffing-recommendation")).toBeInTheDocument();
    expect(screen.queryByText("The network connection was lost.")).not.toBeInTheDocument();
  });

  it("aborts the in-flight run when the user switches away from the tab", async () => {
    const user = await openStaffingTab();
    const call = await startHangingRun(user);
    expect(call.signal?.aborted).toBe(false);

    await user.click(screen.getByRole("tab", { name: "Usage" }));

    expect(call.signal?.aborted).toBe(true);
  });
});
