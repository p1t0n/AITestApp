import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import { selectAgentSurface, currentAgentSurface, SURFACE_PICKER_NAME } from "../test/agentSurface";
import type { AgentDock } from "./useAgentDock";
import type { UsageSnapshot } from "../api";

// The dock's navigation itself (P1T-152). The per-agent specs each drive one surface; this one
// drives the container: that every surface is reachable, that the groups are the ones the flat
// tab strip was hiding, and that the ledger left the strip without leaving the panel.

const usage: UsageSnapshot = {
  daily: { window: "daily", used: 1200, cap: 50000, exceeded: false, resetAt: new Date(Date.now() + 3_600_000).toISOString() },
  weekly: { window: "weekly", used: 1200, cap: 200000, exceeded: false, resetAt: new Date(Date.now() + 86_400_000).toISOString() },
  monthly: { window: "monthly", used: 1200, cap: 800000, exceeded: false, resetAt: new Date(Date.now() + 86_400_000).toISOString() },
  byAgent: [],
};

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false, isSuccess: false, isError: false, error: null });
  return {
    ...actual,
    useUsage: () => ({ data: usage, isLoading: false, isError: false, error: null }),
    useEmployees: () => ({ data: [], isLoading: false }),
    useSkills: () => ({ data: [], isLoading: false }),
    useCategories: () => ({ data: [], isLoading: false }),
    useRosterScanJob: () => ({ data: undefined }),
    useSubmitRosterScan: idle,
    useRosterQa: idle,
    useCvTailoring: idle,
    useMatch: idle,
    useJdMatch: idle,
    useInterviewKit: idle,
    useApplyRewrite: idle,
    useShortlist: idle,
    useBenchReport: idle,
    useResumeIngestion: idle,
    useStaffingProposals: () => ({ data: [], isLoading: false }),
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

function renderWidget(isNarrow = false, docked = false) {
  const user = userEvent.setup();
  render(
    <MemoryRouter>
      <AgentWidget dock={{ ...dock, docked }} isNarrow={isNarrow} />
    </MemoryRouter>,
  );
  return user;
}

/** Every surface, with the marker that proves its panel actually mounted. */
const SURFACES: { label: string; marker: RegExp }[] = [
  { label: "Roster Q&A", marker: /Ask about the roster…/ },
  { label: "Tailor CV", marker: /Paste a job description, or pick a preset above…/ },
  { label: "Match", marker: /Pick an employee, or leave empty to search the roster/ },
  { label: "Interview kit", marker: /Paste a job description, or pick a preset above…/ },
  { label: "Shortlist", marker: /Paste a job description, or pick a preset above…/ },
  { label: "Staffing", marker: /Paste a job description, or pick a preset above…/ },
  { label: "Roster scan", marker: /Paste a job description to scan the whole roster against…/ },
  { label: "Bench report", marker: /Generate bench report/ },
  { label: "Resume ingest", marker: /Paste the raw resume or LinkedIn text…/ },
];

describe("agent dock navigation (P1T-152)", () => {
  it("opens on Roster Q&A", () => {
    renderWidget();
    expect(currentAgentSurface()).toBe("Roster Q&A");
    expect(screen.getByPlaceholderText(/Ask about the roster…/)).toBeInTheDocument();
  });

  it.each(SURFACES)("reaches $label and mounts its panel", async ({ label, marker }) => {
    const user = renderWidget();
    await selectAgentSurface(user, label);

    expect(currentAgentSurface()).toBe(label);
    // Every surface has at least one of: a placeholder or a button carrying its marker.
    const hit =
      screen.queryAllByPlaceholderText(marker).length > 0 ||
      screen.queryAllByText(marker).length > 0 ||
      screen.queryAllByLabelText(marker).length > 0;
    expect(hit).toBe(true);
  });

  it("groups the surfaces by what they act on, and every label is spelled out in full", async () => {
    const user = renderWidget();
    await user.click(screen.getByRole("button", { name: SURFACE_PICKER_NAME }));

    const menu = await screen.findByRole("menu");
    // Group headers are presentational — they label the groups without posing as pickable items.
    for (const category of ["Ask about the roster", "Act on one person", "Act on a role", "Operate"]) {
      expect(menu).toHaveTextContent(category);
    }
    expect(within(menu).getAllByRole("menuitem").map((o) => o.textContent)).toEqual(
      SURFACES.map((s) => s.label),
    );
  });

  it("keeps the token ledger out of the picker and reachable from the header", async () => {
    const user = renderWidget();
    await user.click(screen.getByRole("button", { name: SURFACE_PICKER_NAME }));
    expect(screen.queryByRole("menuitem", { name: /usage/i })).not.toBeInTheDocument();
    await user.keyboard("{Escape}");

    await user.click(screen.getByRole("button", { name: "Token usage" }));
    expect(screen.getByText("This month by agent")).toBeInTheDocument();
    // The ledger is a peek, not a place: the picker steps aside and names the way back.
    expect(screen.queryByRole("button", { name: SURFACE_PICKER_NAME })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Back to Roster Q&A/ }));
    expect(currentAgentSurface()).toBe("Roster Q&A");
  });

  it("discards a surface's state when you navigate away and back (remount-as-reset)", async () => {
    const user = renderWidget();
    await selectAgentSurface(user, "Roster scan");

    const jd = screen.getByPlaceholderText(/Paste a job description to scan the whole roster against…/);
    await user.type(jd, "Senior platform engineer");
    expect(jd).toHaveValue("Senior platform engineer");

    await selectAgentSurface(user, "Bench report");
    await selectAgentSurface(user, "Roster scan");

    expect(
      screen.getByPlaceholderText(/Paste a job description to scan the whole roster against…/),
    ).toHaveValue("");
  });

  it("survives the ledger round-trip the same way", async () => {
    const user = renderWidget();
    await selectAgentSurface(user, "Roster scan");
    await user.type(
      screen.getByPlaceholderText(/Paste a job description to scan the whole roster against…/),
      "Senior platform engineer",
    );

    await user.click(screen.getByRole("button", { name: "Token usage" }));
    await user.click(screen.getByRole("button", { name: /Back to Roster scan/ }));

    expect(currentAgentSurface()).toBe("Roster scan");
    expect(
      screen.getByPlaceholderText(/Paste a job description to scan the whole roster against…/),
    ).toHaveValue("");
  });

  it.each([
    ["floating", false, false],
    ["docked wide", true, false],
    ["docked narrow", true, true],
  ])("renders the picker in the %s layout", async (_name, docked, isNarrow) => {
    const user = renderWidget(isNarrow, docked);
    expect(screen.getByRole("button", { name: SURFACE_PICKER_NAME })).toBeInTheDocument();

    await selectAgentSurface(user, "Bench report");
    expect(currentAgentSurface()).toBe("Bench report");
  });
});
