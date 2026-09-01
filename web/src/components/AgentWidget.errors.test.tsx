import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import type { AgentDock } from "./useAgentDock";
import { SURFACE_PICKER_NAME, selectAgentSurface } from "../test/agentSurface";

// The Usage panel throws on render — a real render-time crash inside one dock panel, not a
// rejected promise (those already flow through each panel's own error state).
vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useUsage: () => {
      throw new Error("usage panel exploded");
    },
    useRosterQa: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useCvTailoring: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useJdMatch: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useInterviewKit: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useApplyRewrite: () => ({ isPending: false, isSuccess: false, isError: false, error: null, mutate: vi.fn() }),
    useExperts: () => ({ data: [], isLoading: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useShortlist: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useBenchReport: () => ({ mutateAsync: vi.fn(), isPending: false }),
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

let consoleError: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
});
afterEach(() => consoleError.mockRestore());

describe("agent dock error containment (P1T-153)", () => {
  it("contains a panel crash to the panel: the widget chrome and the page stay usable", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <div>the roster page</div>
        <AgentWidget dock={dock} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "Token usage" }));

    expect(screen.getByRole("alert")).toHaveTextContent("This panel stopped working");
    expect(screen.getByRole("alert")).toHaveTextContent("usage panel exploded");
    // The page behind the dock, the widget header and its ledger toggle all survive.
    expect(screen.getByText("the roster page")).toBeInTheDocument();
    expect(screen.getByText("Agents")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Token usage" })).toBeInTheDocument();
  });

  it("recovers by leaving the crashed ledger — what is showing is the boundary's reset key", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AgentWidget dock={dock} />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("button", { name: "Token usage" }));
    expect(screen.getByRole("alert")).toHaveTextContent("This panel stopped working");

    // The ledger replaces the picker with its own "Back to <surface>" bar (P1T-152), and that bar
    // is outside the boundary, so it is still there to click.
    await user.click(screen.getByRole("button", { name: /Back to Roster Q&A/ }));
    expect(screen.queryByText("This panel stopped working")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: SURFACE_PICKER_NAME })).toHaveTextContent("Roster Q&A");

    // And from there the picker still navigates: the crash is not sticky.
    await selectAgentSurface(user, "Bench report");
    expect(screen.getByRole("button", { name: SURFACE_PICKER_NAME })).toHaveTextContent("Bench report");
  });
});
