import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import { agentSurfacePane, currentAgentSurface, selectAgentSurface } from "../test/agentSurface";
import type { RosterQaInput, RosterQaResponse, ShortlistResponse, UsageSnapshot } from "../api";

// EXP-30. The dock used to render exactly one panel — a conditional chain — so peeking at the token
// ledger, or stepping to another agent and back, silently threw away whatever was in the panel: a
// conversation, a half-typed JD, a match result. This file is the record of what survives now.
//
// The rule it holds in both directions: a surface is mounted on *first visit* and kept, so nothing
// a person typed is lost; and an unvisited surface is not mounted at all, so keeping state never
// costs a fetch nobody asked for.

const EXPERT_ID = "11111111-2222-3333-4444-555555555555";

const askState = {
  mutateAsync: vi.fn<(input: RosterQaInput) => Promise<RosterQaResponse>>(),
  isPending: false,
};
const shortlistState = {
  mutateAsync: vi.fn<(req: unknown) => Promise<ShortlistResponse>>(),
  isPending: false,
};

const USAGE: UsageSnapshot = {
  daily: { window: "daily", used: 1200, cap: 50000, exceeded: false, resetAt: new Date(Date.now() + 3_600_000).toISOString() },
  weekly: { window: "weekly", used: 1200, cap: 200000, exceeded: false, resetAt: new Date(Date.now() + 86_400_000).toISOString() },
  monthly: { window: "monthly", used: 1200, cap: 800000, exceeded: false, resetAt: new Date(Date.now() + 86_400_000).toISOString() },
  byAgent: [],
};

/** Flipped by the containment test: the ledger is the surface that throws on render. */
let usageExplodes = false;

/** Called once per render of the Bench panel — the lazy-mount claim, as a spy. */
const benchHook = vi.fn();

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  // Declared inside the factory: `vi.mock` is hoisted above every top-level binding in this file,
  // so anything the factory *calls* while it builds the module has to live here.
  const idle = () => ({
    mutateAsync: vi.fn(),
    mutate: vi.fn(),
    isPending: false,
    isSuccess: false,
    isError: false,
    error: null,
  });
  return {
    ...actual,
    // The dock asks which model answers on the current surface (EXP-31); it renders outside a
    // QueryClientProvider here, like every other hook in this factory.
    useAgentModels: () => ({ data: undefined, isError: false }),
    useRosterQa: () => askState,
    useShortlist: () => shortlistState,
    useUsage: () => {
      if (usageExplodes) throw new Error("usage panel exploded");
      return { data: USAGE, isLoading: false, isError: false, error: null };
    },
    useBenchReport: () => {
      benchHook();
      return idle();
    },
    useExperts: () => ({
      data: [
        {
          id: EXPERT_ID,
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
    useCategories: () => ({ data: [], isLoading: false }),
    useRosterScanJob: () => ({ data: undefined }),
    useSubmitRosterScan: idle,
    useCvTailoring: idle,
    useMatch: idle,
    useJdMatch: idle,
    useInterviewKit: idle,
    useApplyRewrite: idle,
    useResumeIngestion: idle,
    useStaffingProposals: () => ({ data: [], isLoading: false }),
  };
});

const SHORTLIST: ShortlistResponse = {
  requirements: ["React expertise"],
  candidates: [
    {
      expertId: EXPERT_ID,
      name: "Ada Lovelace",
      title: "Senior Engineer",
      score: 0.9,
      coverage: { matched: 1, total: 1 },
      requirements: [{ text: "React expertise", matched: true, snippet: "Built React apps for 6 years" }],
      rationale: "Strong React background.",
    },
  ],
};

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

function renderWidget() {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
  return user;
}

const ledgerButton = () => screen.getByRole("button", { name: "Token usage" });

/** Fill the expert + JD of a job form (Tailor CV / Match / Interview kit) in its own pane. */
async function fillJobForm(user: ReturnType<typeof userEvent.setup>, pane: HTMLElement, jd: string) {
  await user.click(within(pane).getByLabelText(/^Expert/));
  await user.click(await screen.findByRole("option", { name: "Ada Lovelace — Senior Engineer" }));
  await user.type(within(pane).getByPlaceholderText(/paste a job description/i), jd);
}

beforeEach(() => {
  usageExplodes = false;
  askState.mutateAsync = vi.fn();
  askState.isPending = false;
  shortlistState.mutateAsync = vi.fn();
  shortlistState.isPending = false;
  benchHook.mockClear();
});

describe("the dock keeps a surface across a ledger peek (EXP-30)", () => {
  it("keeps the transcript, the draft and the thread through a ledger round-trip", async () => {
    const user = renderWidget();
    askState.mutateAsync
      .mockResolvedValueOnce({ answer: "Ada knows React.", threadId: "t-1" })
      .mockResolvedValueOnce({ answer: "Ada is free in July.", threadId: "t-1" });

    await user.type(screen.getByPlaceholderText("Ask about the roster…"), "Who knows React?");
    await user.click(screen.getByLabelText("Send"));
    await screen.findByText("Ada knows React.");
    // A half-typed follow-up is the cheapest thing to lose and the most annoying.
    await user.type(screen.getByPlaceholderText("Ask about the roster…"), "Are they free in July?");

    await user.click(ledgerButton());
    expect(screen.getByText("This month by agent")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Back to Roster Q&A/ }));

    const pane = agentSurfacePane("Roster Q&A");
    expect(within(pane).getByText("Ada knows React.")).toBeInTheDocument();
    expect(within(pane).getByPlaceholderText("Ask about the roster…")).toHaveValue(
      "Are they free in July?",
    );

    // And the conversation is the same conversation, not a new one wearing the old transcript.
    await user.click(within(pane).getByLabelText("Send"));
    await screen.findByText("Ada is free in July.");
    expect(askState.mutateAsync).toHaveBeenLastCalledWith({
      question: "Are they free in July?",
      threadId: "t-1",
    });
  });

  it("keeps a filled-in job form across a surface switch", async () => {
    const user = renderWidget();
    await selectAgentSurface(user, "Tailor CV");
    await fillJobForm(user, agentSurfacePane("Tailor CV"), "Senior React engineer");

    await selectAgentSurface(user, "Shortlist");
    await selectAgentSurface(user, "Tailor CV");

    const pane = agentSurfacePane("Tailor CV");
    expect(within(pane).getByLabelText(/^Expert/)).toHaveValue("Ada Lovelace — Senior Engineer");
    expect(within(pane).getByPlaceholderText(/paste a job description/i)).toHaveValue(
      "Senior React engineer",
    );
  });
});

describe("keeping a surface costs nothing until it is opened (EXP-30)", () => {
  it("never mounts a surface nobody has visited", async () => {
    const user = renderWidget();
    await selectAgentSurface(user, "Shortlist");

    // Nine surfaces exist; two have been opened. The bench report has not fetched, rendered, or
    // put a single control in the document.
    expect(benchHook).not.toHaveBeenCalled();
    expect(screen.queryByText(/Generate bench report/)).not.toBeInTheDocument();

    await selectAgentSurface(user, "Bench report");
    expect(benchHook).toHaveBeenCalled();
  });

  it("forgets the open panes when the dock closes, rather than remounting them on the way back", async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <MemoryRouter>
        <AgentWidget dock={dock} />
      </MemoryRouter>,
    );
    await selectAgentSurface(user, "Bench report");
    expect(document.querySelectorAll("section[aria-label]")).toHaveLength(2);

    // Closing takes the whole panel with it, so every pane's state is gone either way. Reopening
    // must not resurrect a row of surfaces — and re-fetch for them — that would come back empty.
    rerender(
      <MemoryRouter>
        <AgentWidget dock={{ ...dock, open: false }} />
      </MemoryRouter>,
    );
    rerender(
      <MemoryRouter>
        <AgentWidget dock={dock} />
      </MemoryRouter>,
    );

    // One pane, the one it is pointed at — not the Roster Q&A it also had open before.
    expect(
      [...document.querySelectorAll("section[aria-label]")].map((p) => p.getAttribute("aria-label")),
    ).toEqual(["Bench report"]);
  });

  it("exposes only the active pane — the rest are hidden, in layout and in the a11y tree", async () => {
    const user = renderWidget();
    await selectAgentSurface(user, "Tailor CV");
    await selectAgentSurface(user, "Shortlist");

    // Both job forms are mounted and both hold a "paste a job description" field...
    expect(screen.getAllByPlaceholderText(/paste a job description/i).length).toBeGreaterThan(1);
    // ...but only one pane is in the accessibility tree, so `byRole` reaches exactly one surface.
    expect(screen.getAllByRole("region").map((r) => r.getAttribute("aria-label"))).toEqual([
      "Shortlist",
    ]);
    expect(screen.getByRole("button", { name: /build shortlist/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Tailor CV" })).not.toBeInTheDocument();

    // The inactive panes say so on the element itself, which is what takes them out of layout.
    const panes = document.querySelectorAll("section[aria-label]");
    expect([...panes].filter((p) => p.hasAttribute("hidden")).map((p) => p.getAttribute("aria-label")))
      .toEqual(["Roster Q&A", "Tailor CV"]);
  });
});

describe("drill-ins still land their values (EXP-30)", () => {
  it("replaces the Match form even when Match was visited earlier, and only Match's", async () => {
    const user = renderWidget();

    // Match, visited by hand first, with values of its own.
    await selectAgentSurface(user, "Match");
    await user.type(
      within(agentSurfacePane("Match")).getByPlaceholderText(/paste a job description/i),
      "Something else entirely",
    );

    // And a second job form, which the drill-in must not touch.
    await selectAgentSurface(user, "Tailor CV");
    await fillJobForm(user, agentSurfacePane("Tailor CV"), "A tailoring JD");

    shortlistState.mutateAsync.mockResolvedValue(SHORTLIST);
    await selectAgentSurface(user, "Shortlist");
    await user.type(
      within(agentSurfacePane("Shortlist")).getByPlaceholderText(/paste a job description/i),
      "Senior React engineer",
    );
    await user.click(screen.getByRole("button", { name: /build shortlist/i }));
    await user.click(await screen.findByRole("button", { name: /run full match/i }));

    expect(currentAgentSurface()).toBe("Match");
    const match = agentSurfacePane("Match");
    expect(within(match).getByLabelText(/^Expert/)).toHaveValue("Ada Lovelace — Senior Engineer");
    expect(within(match).getByPlaceholderText(/paste a job description/i)).toHaveValue(
      "Senior React engineer",
    );

    // Only Match. The other form kept what was typed into it.
    await selectAgentSurface(user, "Tailor CV");
    expect(
      within(agentSurfacePane("Tailor CV")).getByPlaceholderText(/paste a job description/i),
    ).toHaveValue("A tailoring JD");

    // And the prefill is spent: coming back to Match by hand shows the form, not a re-application.
    await selectAgentSurface(user, "Match");
    expect(within(agentSurfacePane("Match")).getByPlaceholderText(/paste a job description/i)).toHaveValue(
      "Senior React engineer",
    );
  });
});

describe("error containment is per pane (P1T-153, EXP-30)", () => {
  it("shows the fallback in the crashing pane only, and the others keep their state", async () => {
    const user = renderWidget();
    askState.mutateAsync.mockResolvedValue({ answer: "Ada knows React.", threadId: "t-1" });
    await user.type(screen.getByPlaceholderText("Ask about the roster…"), "Who knows React?");
    await user.click(screen.getByLabelText("Send"));
    await screen.findByText("Ada knows React.");

    const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
    usageExplodes = true;
    await user.click(ledgerButton());

    expect(screen.getByRole("alert")).toHaveTextContent("This panel stopped working");
    expect(screen.getByRole("alert")).toHaveTextContent("usage panel exploded");
    // The chrome is outside every pane, so the way out of a crash keeps rendering.
    expect(screen.getByRole("button", { name: /Back to Roster Q&A/ })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Back to Roster Q&A/ }));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(within(agentSurfacePane("Roster Q&A")).getByText("Ada knows React.")).toBeInTheDocument();

    // Opening it again retries it — the pane is re-entered, not resurrected with its old error.
    usageExplodes = false;
    await user.click(ledgerButton());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(within(agentSurfacePane("Token usage")).getByText("This month by agent")).toBeInTheDocument();
    consoleError.mockRestore();
  });
});
