// Which model is answering, shown in the dock (EXP-31) — the two halves of the same question.
//
//   * the **header caption**: what the surface you are looking at is *configured* to use, before
//     you spend anything. It comes from GET /agents/models, which is per-deployment rather than
//     per-user, so the dock knows it without asking the model anything.
//   * the **transcript caption**: on each Roster Q&A answer, the model the provider said actually
//     wrote it. Those two can differ — an alias resolves, a provider answers on a point release —
//     which is exactly why both are shown rather than one standing in for the other.
//
// Neither is allowed to be load-bearing: the query is an aside, and a surface that waited on it or
// threw without it would have turned a caption into a dependency.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type { AgentModels, RosterQaInput, RosterQaResponse } from "../api";
import { selectAgentSurface } from "../test/agentSurface";

const askState = {
  mutateAsync: vi.fn<(input: RosterQaInput) => Promise<RosterQaResponse>>(),
  isPending: false,
};

const modelsState: {
  data: AgentModels | undefined;
  isError: boolean;
} = { data: undefined, isError: false };

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false, isSuccess: false, isError: false, error: null });
  const { rosterQaHistoryMocks } = await import("../test/rosterQaHistory");
  return {
    ...actual,
    // The surface mounts the conversation-history hooks (EXP-34); this file is not about
    // them, and every hook here renders outside a QueryClientProvider.
    ...rosterQaHistoryMocks(),
    useAgentModels: () => modelsState,
    useRosterQa: () => askState,
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useExperts: () => ({ data: [], isLoading: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useCategories: () => ({ data: [], isLoading: false }),
    useCvTailoring: idle,
    useMatch: idle,
    useJdMatch: idle,
    useShortlist: idle,
    useResumeIngestion: idle,
    useBenchReport: idle,
  };
});

const dock: AgentDock = {
  open: true,
  docked: false,
  width: 460,
  isNarrow: false,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

const models: AgentModels = {
  provider: "Gemini",
  surfaces: {
    roster: ["gemini-3.5-flash-lite"],
    "cv-tailoring": ["gemini-3.5-pro"],
    // The multi-agent case: Staffing runs four agents, two of them on their own model.
    staffing: ["gemini-3.5-flash-lite", "gemini-3.5-pro"],
    match: ["gemini-3.5-flash-lite"],
    "interview-kit": ["gemini-3.5-flash-lite"],
    shortlist: ["gemini-3.5-flash-lite"],
    "roster-scan": ["gemini-3.5-flash-lite"],
    bench: ["gemini-3.5-flash-lite"],
    ingestion: ["gemini-3.5-flash-lite"],
  },
};

/** The header bar — the element holding the title, the window controls and the picker. */
function headerBar(): HTMLElement {
  return screen.getByText("Agents").closest("div")!.parentElement!;
}

function renderDock() {
  return render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  modelsState.data = models;
  modelsState.isError = false;
});

describe("the dock header names the surface's configured model", () => {
  it("shows the current surface's model beside the title", () => {
    renderDock();

    expect(within(headerBar()).getByText("gemini-3.5-flash-lite")).toBeInTheDocument();
  });

  it("follows the surface picker", async () => {
    const user = userEvent.setup();
    renderDock();

    await selectAgentSurface(user, "Tailor CV");

    expect(within(headerBar()).getByText("gemini-3.5-pro")).toBeInTheDocument();
    expect(within(headerBar()).queryByText("gemini-3.5-flash-lite")).not.toBeInTheDocument();
  });

  it("joins several models with a middle dot when a surface runs more than one", async () => {
    const user = userEvent.setup();
    renderDock();

    await selectAgentSurface(user, "Staffing");

    expect(
      within(headerBar()).getByText("gemini-3.5-flash-lite · gemini-3.5-pro"),
    ).toBeInTheDocument();
  });

  it("names the provider in its tooltip", async () => {
    const user = userEvent.setup();
    renderDock();

    await user.hover(within(headerBar()).getByText("gemini-3.5-flash-lite"));

    expect(await screen.findByRole("tooltip")).toHaveTextContent("Gemini");
  });

  it("steps aside while the Token Ledger is open, and comes back", async () => {
    const user = userEvent.setup();
    renderDock();

    await user.click(screen.getByRole("button", { name: "Token usage" }));
    expect(within(headerBar()).queryByText("gemini-3.5-flash-lite")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Token usage" }));
    expect(within(headerBar()).getByText("gemini-3.5-flash-lite")).toBeInTheDocument();
  });

  it("shows nothing at all while the models are unknown, and nothing breaks", () => {
    modelsState.data = undefined;
    renderDock();

    // The dock still works: the title, the controls and the surface itself are all there.
    expect(screen.getByText("Agents")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Token usage" })).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Ask about the roster…")).toBeInTheDocument();
    expect(within(headerBar()).queryByText(/gemini/)).not.toBeInTheDocument();
  });

  it("shows nothing when the fetch failed", () => {
    modelsState.data = undefined;
    modelsState.isError = true;
    renderDock();

    expect(within(headerBar()).queryByText(/gemini/)).not.toBeInTheDocument();
    expect(screen.getByPlaceholderText("Ask about the roster…")).toBeInTheDocument();
  });

  it("says nothing about a surface the server did not report", () => {
    modelsState.data = { provider: "Gemini", surfaces: { roster: [] } };
    renderDock();

    expect(within(headerBar()).queryByText(/gemini/)).not.toBeInTheDocument();
  });
});

describe("a Roster Q&A answer names the model that wrote it", () => {
  async function ask(text: string) {
    await userEvent.type(screen.getByPlaceholderText("Ask about the roster…"), text);
    await userEvent.click(screen.getByLabelText("Send"));
  }

  it("captions the assistant turn with the reported model", async () => {
    askState.mutateAsync.mockResolvedValueOnce({
      answer: "Ada knows React.",
      threadId: "t-1",
      modelId: "gemini-3.5-flash-lite-002",
    });
    renderDock();

    await ask("Who knows React?");

    const answer = await screen.findByText("Ada knows React.");
    // On the answer's own bubble, not loose in the transcript: the caption belongs to one turn.
    expect(within(answer.closest(".MuiPaper-root")!).getByText("gemini-3.5-flash-lite-002"))
      .toBeInTheDocument();
  });

  it("shows no caption when the server reported no model", async () => {
    askState.mutateAsync.mockResolvedValueOnce({
      answer: "Ada knows React.",
      threadId: "t-1",
      modelId: null,
    });
    renderDock();

    await ask("Who knows React?");

    const answer = await screen.findByText("Ada knows React.");
    expect(within(answer.closest(".MuiPaper-root")!).queryByText(/gemini/)).not.toBeInTheDocument();
  });

  it("captions the answer and never the question or an error", async () => {
    askState.mutateAsync
      .mockResolvedValueOnce({ answer: "Ada knows React.", threadId: "t-1", modelId: "model-x" })
      .mockRejectedValueOnce(new Error("upstream is down"));
    renderDock();

    await ask("Who knows React?");
    await screen.findByText("Ada knows React.");
    await ask("And in July?");
    await screen.findByText(/upstream is down/);

    // One caption in the whole transcript: the one answer that came back with a model.
    expect(screen.getAllByText("model-x")).toHaveLength(1);
  });
});
