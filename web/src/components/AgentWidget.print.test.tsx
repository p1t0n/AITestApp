import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { printBlockFor } from "../test/printCss";
import type { AgentDock } from "./useAgentDock";

// The dock's print rules (P1T-166). It had none: both of its surfaces are `position: fixed`, so
// they are painted over the page rather than laid out in it, and the closed state's bubble was
// landing in the bottom-right corner of the first sheet of every printed artifact in this app —
// including a client's CV. Found by watching Chromium resolve the cascade at print media
// (`web/e2e/print.e2e.ts`), not by reading a diff.
//
// These are the cheap half of that check and deliberately so: they prove the declaration is
// emitted and attached to the element's own class, which is all jsdom can answer (see
// `src/test/printCss.ts`). They exist because the e2e suite needs a database and a browser and so
// does not run by default — without them, deleting the rule again is a green `npm test`.

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false, isSuccess: false, isError: false, error: null });
  return {
    ...actual,
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useEmployees: () => ({ data: [], isLoading: false }),
    useRosterQa: idle,
    useStaffingProposals: () => ({ data: [], isLoading: false }),
  };
});

const dock: AgentDock = {
  open: false,
  docked: false,
  width: 420,
  isNarrow: false,
  toggleOpen: () => {},
  close: () => {},
  setDocked: () => {},
  setWidth: () => {},
};

function renderWidget(over: Partial<AgentDock> = {}) {
  render(
    <MemoryRouter>
      <AgentWidget dock={{ ...dock, ...over }} />
    </MemoryRouter>,
  );
}

describe("the agent dock is not part of a printed document", () => {
  it("keeps the closed state's bubble off the page", () => {
    renderWidget();

    const fab = screen.getByRole("button", { name: "Open the agents assistant" });

    expect(printBlockFor(fab)).toContain("display:none");
  });

  // Both open shapes, because they are different `sx` objects: a docked sidebar carries a
  // `borderLeft`, which print keeps even where it drops the surface colour behind it (P1T-160),
  // and a floating panel carries a radius and a shadow. One rule has to cover both.
  it.each([
    ["docked to the side", { open: true, docked: true }],
    ["floating over the app", { open: true, docked: false }],
  ] as const)("keeps the open panel off the page when %s", (_shape, over) => {
    renderWidget(over);

    const panel = screen.getByRole("button", { name: "Token usage" }).closest(".MuiPaper-root");
    expect(panel).not.toBeNull();

    expect(printBlockFor(panel!)).toContain("display:none");
  });
});
