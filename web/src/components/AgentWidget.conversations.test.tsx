// Conversation history in the Roster Q&A surface (EXP-34, ADR §7).
//
// The surface stopped being a transcript that dies with the tab: it opens onto a durable
// conversation, names it, and can reach every other one the owner has. Nine claims below, one per
// thing the ticket asks for, and each is about what a person sees or what the next request
// carries — the arithmetic underneath (grouping, the countdown, the resume window) is asserted
// without a DOM in `agent/conversationHistory.test.ts`.
//
// The API hooks are mocked, as every dock spec mocks them: this file is about the surface's own
// behaviour, and `api/agents/conversations.test.tsx` already holds the URLs and the invalidation.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import type {
  ConversationDetail,
  ConversationSummary,
  RosterQaInput,
  RosterQaResponse,
} from "../api";

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
const ago = (ms: number) => new Date(Date.now() - ms).toISOString();
const ahead = (ms: number) => new Date(Date.now() + ms).toISOString();

const askState = {
  mutateAsync: vi.fn<(input: RosterQaInput) => Promise<RosterQaResponse>>(),
  isPending: false,
};
const listState: { data: ConversationSummary[] | undefined; isLoading: boolean } = {
  data: [],
  isLoading: false,
};
const detailState: { data: ConversationDetail | undefined; isLoading: boolean } = {
  data: undefined,
  isLoading: false,
};
/** Which conversation the surface asked the history API for — `null` means it asked for none. */
const detailRequestedFor = vi.fn<(id: string | null | undefined) => void>();
const deleteOne = vi.fn<(id: string) => void>();
const deleteAll = vi.fn<() => void>();

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false, isSuccess: false, isError: false, error: null });
  return {
    ...actual,
    useAgentModels: () => ({ data: undefined, isError: false }),
    useRosterQa: () => askState,
    useRosterQaConversations: () => listState,
    useRosterQaConversation: (id: string | null | undefined) => {
      detailRequestedFor(id);
      return id ? detailState : { data: undefined, isLoading: false };
    },
    useDeleteRosterQaConversation: () => ({ ...idle(), mutate: deleteOne }),
    useDeleteAllRosterQaConversations: () => ({ ...idle(), mutate: deleteAll }),
    useExperts: () => ({ data: [], isLoading: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useMatch: idle,
    useCvTailoring: idle,
    useShortlist: idle,
    useResumeIngestion: idle,
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

const summary = (over: Partial<ConversationSummary> & { id: string }): ConversationSummary => ({
  title: `Conversation ${over.id}`,
  createdAt: ago(DAY),
  lastActiveAt: ago(DAY),
  expiresAt: ahead(120 * DAY),
  ...over,
});

function renderDock() {
  return render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
}

async function openHistory(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "Conversation history" }));
  return screen.getByRole("region", { name: "Conversation history" });
}

async function ask(user: ReturnType<typeof userEvent.setup>, text: string) {
  await user.type(screen.getByPlaceholderText("Ask about the roster…"), text);
  await user.click(screen.getByLabelText("Send"));
}

beforeEach(() => {
  vi.clearAllMocks();
  listState.data = [];
  listState.isLoading = false;
  detailState.data = undefined;
  detailState.isLoading = false;
});

describe("the surface header", () => {
  it("names the conversation, and says so plainly when there is none", async () => {
    renderDock();

    expect(screen.getByRole("button", { name: "New conversation" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Conversation history" })).toBeInTheDocument();
    expect(screen.getByText("New conversation", { selector: "p,span,div" })).toBeInTheDocument();
  });

  it("shows the open conversation's title, ellipsized rather than wrapped", async () => {
    const user = userEvent.setup();
    const long = summary({ id: "c1", title: "Who knows React and is free this summer in Berlin?" });
    listState.data = [long];
    detailState.data = { id: "c1", title: long.title, lastActiveAt: long.lastActiveAt, expiresAt: long.expiresAt, turns: [] };
    renderDock();

    await user.click(within(await openHistory(user)).getByRole("button", { name: /^Who knows React/ }));

    const title = await screen.findByTitle(long.title);
    expect(title).toHaveTextContent(long.title);
    expect(getComputedStyle(title).textOverflow).toBe("ellipsis");
  });
});

describe("the history drawer", () => {
  it("groups rows by Today, This week and Older", async () => {
    const user = userEvent.setup();
    listState.data = [
      summary({ id: "today", title: "Asked this morning", lastActiveAt: ago(2 * HOUR) }),
      summary({ id: "week", title: "Asked on Monday", lastActiveAt: ago(3 * DAY) }),
      summary({ id: "old", title: "Asked last month", lastActiveAt: ago(40 * DAY) }),
    ];
    renderDock();

    const drawer = await openHistory(user);
    const headings = within(drawer).getAllByRole("heading", { level: 3 }).map((h) => h.textContent);
    expect(headings).toEqual(["Today", "This week", "Older"]);

    // Each row under its own heading, in that order — the grouping is the navigation.
    const rows = within(drawer).getAllByRole("button", { name: /^Asked/ }).map((r) => r.textContent);
    expect(rows[0]).toContain("Asked this morning");
    expect(rows[1]).toContain("Asked on Monday");
    expect(rows[2]).toContain("Asked last month");
  });

  it("leaves out a group nothing falls into", async () => {
    const user = userEvent.setup();
    listState.data = [summary({ id: "old", title: "Asked last month", lastActiveAt: ago(40 * DAY) })];
    renderDock();

    const drawer = await openHistory(user);
    expect(within(drawer).getAllByRole("heading", { level: 3 }).map((h) => h.textContent))
      .toEqual(["Older"]);
  });

  it("warns about an expiry inside 14 days, and about nothing further out", async () => {
    const user = userEvent.setup();
    listState.data = [
      summary({ id: "soon", title: "Going soon", expiresAt: ahead(3 * DAY) }),
      summary({ id: "later", title: "Going later", expiresAt: ahead(90 * DAY) }),
    ];
    renderDock();

    const drawer = await openHistory(user);
    const hint = within(drawer).getByText(/disappears in 3 d/);
    expect(hint).toBeInTheDocument();
    expect(within(drawer).queryByText(/disappears in 90 d/)).not.toBeInTheDocument();
    expect(within(drawer).getAllByText(/disappears in/)).toHaveLength(1);
  });

  it("says what retention does, in the drawer that can undo it", async () => {
    const user = userEvent.setup();
    renderDock();

    expect(
      within(await openHistory(user))
        .getByText("Conversations are deleted 6 months after their last activity."),
    ).toBeInTheDocument();
  });

  it("deletes one row on the spot, with nothing to confirm", async () => {
    const user = userEvent.setup();
    listState.data = [summary({ id: "c1", title: "Who knows React?" })];
    renderDock();

    const drawer = await openHistory(user);
    await user.click(
      within(drawer).getByRole("button", { name: 'Delete conversation "Who knows React?"' }),
    );

    expect(deleteOne).toHaveBeenCalledWith("c1");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    // …and it did not also open the conversation it just deleted.
    expect(detailRequestedFor).not.toHaveBeenCalledWith("c1");
  });

  it("asks before deleting all of them, and does nothing until the answer is yes", async () => {
    const user = userEvent.setup();
    listState.data = [summary({ id: "c1" }), summary({ id: "c2" })];
    renderDock();

    const drawer = await openHistory(user);
    await user.click(within(drawer).getByRole("button", { name: "Delete all conversations" }));

    const dialog = await screen.findByRole("dialog");
    expect(deleteAll).not.toHaveBeenCalled();

    await user.click(within(dialog).getByRole("button", { name: "Cancel" }));
    // The rest of the app is `aria-hidden` while a MUI dialog is up, so the footer button is not
    // reachable by role again until the close transition has finished.
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(deleteAll).not.toHaveBeenCalled();

    await user.click(within(drawer).getByRole("button", { name: "Delete all conversations" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Delete all" }));
    expect(deleteAll).toHaveBeenCalledTimes(1);
  });

  it("closes without choosing anything", async () => {
    const user = userEvent.setup();
    renderDock();

    const drawer = await openHistory(user);
    await user.click(within(drawer).getByRole("button", { name: "Close history" }));

    expect(screen.queryByRole("region", { name: "Conversation history" })).not.toBeInTheDocument();
    expect(detailRequestedFor).toHaveBeenLastCalledWith(null);
  });
});

describe("opening a past conversation", () => {
  it("loads it, closes the drawer, and continues it with the next question", async () => {
    const user = userEvent.setup();
    listState.data = [summary({ id: "c7", title: "Who knows React?" })];
    detailState.data = {
      id: "c7",
      title: "Who knows React?",
      lastActiveAt: ago(3 * DAY),
      expiresAt: ahead(90 * DAY),
      turns: [
        {
          question: "Who knows React?",
          answer: "Ada Lovelace does.",
          modelId: "gemini-3.5-flash-lite",
          grounded: true,
          createdAt: ago(3 * DAY),
          state: "ok",
        },
      ],
    };
    askState.mutateAsync.mockResolvedValueOnce({ answer: "She is free in July.", threadId: "c7" });
    renderDock();

    const drawer = await openHistory(user);
    await user.click(within(drawer).getByRole("button", { name: /^Who knows React\?/ }));

    await waitFor(() =>
      expect(screen.queryByRole("region", { name: "Conversation history" })).not.toBeInTheDocument());
    expect(await screen.findByText("Ada Lovelace does.")).toBeInTheDocument();

    await ask(user, "Is she free in July?");
    await screen.findByText("She is free in July.");
    expect(askState.mutateAsync).toHaveBeenCalledWith({
      question: "Is she free in July?",
      threadId: "c7",
    });
  });

  it("New conversation empties the surface and stops sending the old id", async () => {
    const user = userEvent.setup();
    listState.data = [summary({ id: "c7", title: "Who knows React?" })];
    detailState.data = {
      id: "c7",
      title: "Who knows React?",
      lastActiveAt: ago(3 * DAY),
      expiresAt: ahead(90 * DAY),
      turns: [{ question: "Who knows React?", answer: "Ada Lovelace does.", modelId: "m", grounded: true, createdAt: ago(3 * DAY), state: "ok" }],
    };
    askState.mutateAsync.mockResolvedValueOnce({ answer: "Fresh answer.", threadId: "c9" });
    renderDock();

    const drawer = await openHistory(user);
    await user.click(within(drawer).getByRole("button", { name: /^Who knows React\?/ }));
    await screen.findByText("Ada Lovelace does.");

    await user.click(screen.getByRole("button", { name: "New conversation" }));
    expect(screen.queryByText("Ada Lovelace does.")).not.toBeInTheDocument();

    await ask(user, "Something else entirely");
    await screen.findByText("Fresh answer.");
    expect(askState.mutateAsync).toHaveBeenLastCalledWith({
      question: "Something else entirely",
      threadId: undefined,
    });
  });
});

describe("what the surface opens onto", () => {
  it("re-opens the conversation that was live half an hour ago", async () => {
    listState.data = [summary({ id: "warm", lastActiveAt: ago(10 * 60_000) })];
    renderDock();

    await waitFor(() => expect(detailRequestedFor).toHaveBeenCalledWith("warm"));
  });

  it("starts fresh when the last one has gone cold", async () => {
    listState.data = [summary({ id: "cold", lastActiveAt: ago(2 * HOUR) })];
    renderDock();

    await waitFor(() => expect(detailRequestedFor).toHaveBeenCalled());
    expect(detailRequestedFor).not.toHaveBeenCalledWith("cold");
  });
});

describe("how a stored turn reads back", () => {
  const base = {
    id: "c1",
    title: "A conversation",
    lastActiveAt: ago(DAY),
    expiresAt: ahead(90 * DAY),
  };

  async function open(turns: ConversationDetail["turns"]) {
    listState.data = [summary({ id: "c1", title: "A conversation", lastActiveAt: ago(10 * 60_000) })];
    detailState.data = { ...base, turns };
    renderDock();
    await screen.findByTitle("A conversation");
  }

  it("replaces both bubbles of an erased turn with one muted line", async () => {
    await open([
      { question: "", answer: "", modelId: "m", grounded: true, createdAt: ago(DAY), state: "removed" },
    ]);

    const line = await screen.findByText("Removed: referred to someone whose data was erased.");
    expect(getComputedStyle(line).fontStyle).toBe("italic");
    // One line, not a question bubble and an answer bubble with nothing in them.
    const transcript = screen.getByRole("log", { name: "Roster Q&A transcript" });
    expect(transcript.querySelectorAll(".MuiPaper-root")).toHaveLength(0);
  });

  it("says a paused profile is why, without saying whose", async () => {
    await open([
      { question: "", answer: "", modelId: "m", grounded: true, createdAt: ago(DAY), state: "hidden" },
    ]);

    expect(
      await screen.findByText("Hidden: referred to someone who has paused their profile."),
    ).toBeInTheDocument();
  });

  it("marks an ungrounded answer with a dashed warning edge and a chip", async () => {
    await open([
      {
        question: "Who knows Rust?",
        answer: "Nobody in the roster does.",
        modelId: "gemini-3.5-flash-lite",
        grounded: false,
        createdAt: ago(DAY),
        state: "ok",
      },
    ]);

    const bubble = (await screen.findByText("Nobody in the roster does.")).closest(".MuiPaper-root")!;
    expect(getComputedStyle(bubble).borderStyle).toBe("dashed");
    expect(within(bubble as HTMLElement).getByText("Not grounded")).toBeInTheDocument();
  });

  it("leaves a grounded answer alone", async () => {
    await open([
      {
        question: "Who knows React?",
        answer: "Ada Lovelace does.",
        modelId: "gemini-3.5-flash-lite",
        grounded: true,
        createdAt: ago(DAY),
        state: "ok",
      },
    ]);

    const bubble = (await screen.findByText("Ada Lovelace does.")).closest(".MuiPaper-root")!;
    expect(getComputedStyle(bubble).borderStyle).not.toBe("dashed");
    expect(within(bubble as HTMLElement).queryByText("Not grounded")).not.toBeInTheDocument();
  });

  it("captions every stored answer with the model that wrote it and when", async () => {
    await open([
      {
        question: "Who knows React?",
        answer: "Ada Lovelace does.",
        modelId: "gemini-3.5-flash-lite",
        grounded: true,
        createdAt: ago(2 * HOUR),
        state: "ok",
      },
    ]);

    const bubble = (await screen.findByText("Ada Lovelace does.")).closest(".MuiPaper-root")!;
    const caption = within(bubble as HTMLElement).getByText("gemini-3.5-flash-lite").parentElement!;
    expect(caption).toHaveTextContent("gemini-3.5-flash-lite · 2 h ago");
  });
});
