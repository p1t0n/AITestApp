import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { selectAgentSurface } from "../test/agentSurface";
import type { AgentDock } from "./useAgentDock";
import type {
  HandoffPackage,
  StaffingProposalDetail,
  StaffingProposalSummary,
} from "../api";

// ---- api module mock ----
// The approval inbox + drill-in (P1T-135). The inbox hook and the drill-in fetch are scripted;
// decisions are recorded. Everything the Staffing tab (and the widget's default tab) touches is
// mocked so no QueryClient or network is needed — the established widget-test pattern.

const ADA = "11111111-2222-3333-4444-555555555555";
const GRACE = "66666666-7777-8888-9999-aaaaaaaaaaaa";
const PROPOSAL_ID = "dddddddd-1111-2222-3333-444444444444";
const CALLER = "99999999-8888-7777-6666-555555555555";

const inbox = {
  data: [] as StaffingProposalSummary[],
  refetch: vi.fn(),
};

const details = {
  calls: [] as string[],
  impl: ((id: string) => Promise.reject(new Error(`no detail scripted for ${id}`))) as (
    id: string,
  ) => Promise<StaffingProposalDetail>,
};

const decisions = {
  calls: [] as { proposalId: string; decision: string }[],
};

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useStaffingProposals: () => ({ data: inbox.data, isLoading: false, refetch: inbox.refetch }),
    getStaffingProposal: (id: string) => {
      details.calls.push(id);
      return details.impl(id);
    },
    decideStaffingProposal: (proposalId: string, decision: "approved" | "rejected") => {
      decisions.calls.push({ proposalId, decision });
      return Promise.resolve({ id: proposalId, status: decision });
    },
    runStaffing: vi.fn(() => Promise.resolve()),
    useSkills: () => ({ data: [], isLoading: false }),
    useEmployees: () => ({ data: [], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useJdMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const SUMMARY: StaffingProposalSummary = {
  id: PROPOSAL_ID,
  jobDescription: "Senior React engineer with leadership experience.",
  status: "pending",
  createdAt: "2026-08-16T12:00:00Z",
  recommendedEmployeeId: ADA,
  reportDegraded: false,
  candidates: [
    {
      employeeId: ADA,
      name: "Ada Lovelace",
      title: "Senior Engineer",
      rank: 1,
      matchScore: 78,
      matchBand: "Strong",
      rationale: "Best coverage.",
    },
  ],
};

const PACKAGE: HandoffPackage = {
  inputs: { jobDescription: "Senior React engineer with leadership experience.", matchTop: "2" },
  report: {
    requirements: ["React expertise", "Team leadership"],
    candidates: [
      {
        employeeId: ADA,
        name: "Ada Lovelace",
        title: "Senior Engineer",
        shortlist: {
          score: 0.92,
          coverage: { matched: 2, total: 2 },
          requirements: [
            { text: "React expertise", matched: true, snippet: "Built React apps for 6 years" },
            { text: "Team leadership", matched: true, snippet: "Led a team of 5" },
          ],
        },
        match: { status: "completed", score: 78, band: "Strong", answer: "## Fit\n\nGreat.", error: null },
        rationale: "Best coverage.",
      },
      {
        employeeId: GRACE,
        name: "Grace Hopper",
        title: "Compiler Engineer",
        shortlist: {
          score: 0.81,
          coverage: { matched: 1, total: 2 },
          requirements: [{ text: "React expertise", matched: false }],
        },
        match: { status: "failed", score: null, band: null, answer: null, error: "model timeout" },
        rationale: "Systems depth, weaker frontend evidence.",
      },
    ],
    recommendation: { employeeId: ADA, narrative: "Ada is the strongest fit overall." },
    degraded: true,
    notes: ["Match failed for Grace Hopper: model timeout"],
    proposalId: PROPOSAL_ID,
    extraction: {
      requirements: [
        {
          text: "React expertise",
          kind: "Skill",
          priority: "MustHave",
          minYears: null,
          evidenceSpan: "React",
          inferred: false,
        },
      ],
      seniority: "Senior",
      location: null,
      ambiguities: [],
    },
  },
  provenance: {
    callerUserId: CALLER,
    capsSnapshotAtStart: [{ window: "daily", used: 1000, cap: 50000, resetAt: "2026-08-17T00:00:00Z" }],
    startedAt: "2026-08-16T12:00:00Z",
  },
  slices: [
    {
      stage: "shortlist",
      agentClientId: "agent-shortlist",
      scopes: ["mcp:read", "mcp:search"],
      modelId: "gemini-3.5-flash-lite",
      inputTokens: 100,
      outputTokens: 20,
      startedAt: "2026-08-16T12:00:01Z",
      completedAt: "2026-08-16T12:00:03Z",
      status: "completed",
    },
    {
      stage: "match",
      agentClientId: "agent-match",
      scopes: ["mcp:read"],
      modelId: null,
      inputTokens: 0,
      outputTokens: 0,
      startedAt: "2026-08-16T12:00:03Z",
      completedAt: "2026-08-16T12:00:06Z",
      status: "failed",
      degradeReason: "model timeout",
      retryCount: 2,
    },
  ],
  degradations: [
    { stage: "match", whatWasLost: "The match assessment for Grace Hopper", why: "model timeout" },
  ],
};

const DETAIL: StaffingProposalDetail = { ...SUMMARY, package: PACKAGE };

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

async function openStaffingTab() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
  await selectAgentSurface(user, "Staffing");
  return user;
}

async function openDrillIn(user: Awaited<ReturnType<typeof openStaffingTab>>) {
  await user.click(screen.getByRole("button", { name: /pending proposals \(1\)/i }));
  await user.click(screen.getByRole("button", { name: "Review" }));
  return screen.findByTestId("proposal-drill-in");
}

beforeEach(() => {
  inbox.data = [SUMMARY];
  inbox.refetch = vi.fn();
  details.calls = [];
  details.impl = () => Promise.resolve(DETAIL);
  decisions.calls = [];
});

describe("approval inbox + drill-in (P1T-135)", () => {
  it("lists pending proposals in the inbox", async () => {
    const user = await openStaffingTab();

    const inboxSection = screen.getByTestId("proposal-inbox");
    await user.click(within(inboxSection).getByRole("button", { name: /pending proposals \(1\)/i }));

    const row = screen.getByTestId(`proposal-row-${PROPOSAL_ID}`);
    within(row).getByText("Senior React engineer with leadership experience.");
    within(row).getByText(/1 candidate/);
  });

  it("drills into the package: recommendation, candidates, extraction, provenance, degradations", async () => {
    const user = await openStaffingTab();
    await openDrillIn(user);

    expect(details.calls).toEqual([PROPOSAL_ID]);

    // The full report renders through the same components the live run uses.
    const recommendation = screen.getByTestId("staffing-recommendation");
    within(recommendation).getByText("Ada is the strongest fit overall.");
    screen.getByTestId(`staffing-candidate-${ADA}`);
    screen.getByTestId(`staffing-candidate-${GRACE}`);

    // The extraction chips (how the JD was read) come from the package's report.
    screen.getByText("How the JD was read");

    // Provenance: when, by whom, model, cost, caps at start.
    const provenance = screen.getByTestId("proposal-provenance");
    expect(provenance.textContent).toContain("gemini-3.5-flash-lite");
    expect(provenance.textContent).toContain("120 tokens");
    expect(provenance.textContent).toContain("daily cap 1,000/50,000 at start");
    expect(provenance.textContent).toContain(`by ${CALLER.slice(0, 8)}`);

    // Degradations render as the familiar amber notes.
    const degradations = screen.getByTestId("proposal-degradations");
    expect(degradations.textContent).toContain("The match assessment for Grace Hopper");
    expect(degradations.textContent).toContain("model timeout");
  });

  it("approves from the drill-in and refreshes the inbox", async () => {
    const user = await openStaffingTab();
    await openDrillIn(user);

    const card = screen.getByTestId("proposal-decision");
    await user.click(within(card).getByRole("button", { name: "Approve" }));

    expect(decisions.calls).toEqual([{ proposalId: PROPOSAL_ID, decision: "approved" }]);
    await screen.findByTestId("proposal-decided");
    expect(inbox.refetch).toHaveBeenCalled();
  });

  it("renders a package-less (pre-migration) proposal honestly from its snapshot", async () => {
    details.impl = () => Promise.resolve({ ...SUMMARY, package: null });
    const user = await openStaffingTab();
    await openDrillIn(user);

    screen.getByTestId("proposal-no-package");
    screen.getByText(/predates handoff packages/i);
    screen.getByText(/#1 Ada Lovelace — Senior Engineer/);
    // The decision is still the human's to make — the snapshot suffices for the ledger.
    screen.getByTestId("proposal-decision");
    expect(screen.queryByTestId("staffing-recommendation")).toBeNull();
  });

  it("shows no inbox section when nothing is pending", async () => {
    inbox.data = [];
    await openStaffingTab();

    expect(screen.queryByTestId("proposal-inbox")).toBeNull();
  });
});
