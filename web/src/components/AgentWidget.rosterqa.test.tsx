import { beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { DOCK_PUSH_VAR, type AgentDock } from "./useAgentDock";
import type { RosterQaInput, RosterQaResponse } from "../api";

const askState = {
  mutateAsync: vi.fn<(input: RosterQaInput) => Promise<RosterQaResponse>>(),
  isPending: false,
};

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useRosterQa: () => askState,
    useExperts: () => ({ data: [], isLoading: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useResumeIngestion: () => ({ mutateAsync: vi.fn(), isPending: false }),
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

async function ask(text: string) {
  await userEvent.type(screen.getByPlaceholderText("Ask about the roster…"), text);
  await userEvent.click(screen.getByLabelText("Send"));
}

beforeEach(() => {
  vi.clearAllMocks();
  render(
    <MemoryRouter>
      <AgentWidget dock={dock} />
    </MemoryRouter>,
  );
});

describe("Roster Q&A threading", () => {
  it("carries the returned threadId into the follow-up question", async () => {
    askState.mutateAsync
      .mockResolvedValueOnce({ answer: "Ada knows React.", threadId: "t-1" })
      .mockResolvedValueOnce({ answer: "Ada is free in July.", threadId: "t-1" });

    await ask("Who knows React?");
    await screen.findByText("Ada knows React.");
    await ask("Are they free in July?");
    await screen.findByText("Ada is free in July.");

    expect(askState.mutateAsync).toHaveBeenNthCalledWith(1, {
      question: "Who knows React?",
      threadId: undefined,
    });
    expect(askState.mutateAsync).toHaveBeenNthCalledWith(2, {
      question: "Are they free in July?",
      threadId: "t-1",
    });
  });

  it("notices an expired thread when the server returns a different id", async () => {
    askState.mutateAsync
      .mockResolvedValueOnce({ answer: "first", threadId: "t-1" })
      .mockResolvedValueOnce({ answer: "second", threadId: "t-2" });

    await ask("first question");
    await screen.findByText("first");
    await ask("follow-up");

    await screen.findByText("That conversation expired — starting a new one.");
    await screen.findByText("second");
  });

  it("New conversation clears the transcript and drops the threadId", async () => {
    askState.mutateAsync
      .mockResolvedValueOnce({ answer: "first", threadId: "t-1" })
      .mockResolvedValueOnce({ answer: "fresh", threadId: "t-9" });

    await ask("first question");
    await screen.findByText("first");
    await userEvent.click(screen.getByRole("button", { name: "New conversation" }));

    expect(screen.queryByText("first")).not.toBeInTheDocument();

    await ask("brand new question");
    await screen.findByText("fresh");
    expect(askState.mutateAsync).toHaveBeenLastCalledWith({
      question: "brand new question",
      threadId: undefined,
    });
  });
});

describe("dock layout push (P1T-154)", () => {
  it("publishes the width it covers, so nothing upstream has to know the dock's mode", () => {
    // The shared render above is a floating bubble: it overlays the app on purpose, pushing nothing.
    expect(document.documentElement.style.getPropertyValue(DOCK_PUSH_VAR)).toBe("0px");

    cleanup();
    render(
      <MemoryRouter>
        <AgentWidget dock={{ ...dock, docked: true, width: 500 }} />
      </MemoryRouter>,
    );

    expect(document.documentElement.style.getPropertyValue(DOCK_PUSH_VAR)).toBe("500px");
  });
});
