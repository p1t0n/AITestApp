import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@mui/material";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import AgentWidget from "./AgentWidget";
import CommandPalette, { PEOPLE_SHOWN } from "./CommandPalette";
import { NAV } from "./AppRail";
import { SURFACE_GROUPS } from "./AgentWidget";
import { requestAgentSurface, useAgentSurfaceRequest } from "./agent/surfaceRequest";
import {
  PALETTE_INPUT_LABEL,
  closeCommandPalette,
  openCommandPalette,
} from "./useCommandPalette";
import { currentAgentSurface } from "../test/agentSurface";
import type { AgentDock } from "./useAgentDock";
import { lightTheme } from "../theme";
import type { ExpertSummary } from "../types";

// The ⌘K Command Palette (P1T-165). What is asserted here is the three kinds of jump, the keyboard
// path through them, and the one claim the feature rests on: the palette searches the whole roster
// rather than a page of it. The last test in the file is the only one that renders a real dock —
// it is what proves a Surface Request is honoured rather than merely sent.

function person(over: Partial<ExpertSummary> & { id: string }): ExpertSummary {
  return {
    firstName: "Ada",
    lastName: "Lovelace",
    title: "Engineer",
    location: "London",
    email: "ada@example.com",
    currentCapacityPercent: 100,
    status: "Active",
    ...over,
  };
}

const ROSTER: ExpertSummary[] = [
  person({ id: "e1", firstName: "Grace", lastName: "Hopper", title: "Rear Admiral", location: "Arlington", email: "grace@navy.example" }),
  person({ id: "e2", firstName: "Ada", lastName: "Lovelace", title: "Analyst", location: "London", email: "ada@example.com" }),
  person({ id: "e3", firstName: "Alan", lastName: "Turing", title: "Cryptanalyst", location: "Bletchley", email: "alan@example.com" }),
];

let roster: ExpertSummary[] = ROSTER;
let rosterLoading = false;

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false, isSuccess: false, isError: false, error: null });
  return {
    ...actual,
    // Read through a getter so a test can change the roster without re-mocking the module.
    useExperts: () => ({ data: roster, isLoading: rosterLoading, isError: false, error: null }),
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
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

function dockWith(over: Partial<AgentDock> = {}): AgentDock {
  return {
    open: false,
    docked: false,
    width: 420,
    isNarrow: false,
    toggleOpen: vi.fn(),
    close: vi.fn(),
    setDocked: vi.fn(),
    setWidth: vi.fn(),
    ...over,
  };
}

/** Where the router ended up, as text — the palette's jumps are asserted through this. */
function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>;
}

function renderPalette(dock: AgentDock = dockWith()) {
  const user = userEvent.setup();
  render(
    <ThemeProvider theme={lightTheme}>
      <MemoryRouter initialEntries={["/catalog"]}>
        <CommandPalette dock={dock} />
        <Where />
        <Routes>
          <Route path="*" element={null} />
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
  );
  return { user };
}

/** The store is outside React, so a programmatic open is flushed like any other external change. */
const open = () => act(() => openCommandPalette());

// Named rather than bare `combobox`: the dock renders `Select`s of its own, and one test has
// both surfaces on screen at once.
const input = () => screen.getByRole("combobox", { name: PALETTE_INPUT_LABEL });
/**
 * The dialog animates out, so "it is gone" is a `waitFor` and not an instant read — and until it
 * is, MUI's modal still holds `aria-hidden` over the rest of the app, which is what makes the dock
 * unreachable by role in the meantime.
 */
const gone = () =>
  waitFor(() =>
    expect(screen.queryByRole("combobox", { name: PALETTE_INPUT_LABEL })).not.toBeInTheDocument(),
  );
const options = () => screen.getAllByRole("option");
const optionNames = () => options().map((o) => o.textContent);
const where = () => screen.getByTestId("where").textContent;

beforeEach(() => {
  roster = ROSTER;
  rosterLoading = false;
});
afterEach(() => closeCommandPalette());

describe("opening and closing", () => {
  it("opens on ⌘K and closes on the same keystroke", async () => {
    renderPalette();

    expect(screen.queryByRole("combobox", { name: PALETTE_INPUT_LABEL })).not.toBeInTheDocument();
    fireEvent.keyDown(window, { key: "k", metaKey: true });
    expect(input()).toBeInTheDocument();

    fireEvent.keyDown(window, { key: "k", metaKey: true });
    await gone();
  });

  it("opens on Ctrl+K too — the shortcut is not a platform quiz", () => {
    renderPalette();

    fireEvent.keyDown(window, { key: "K", ctrlKey: true });
    expect(input()).toBeInTheDocument();
  });

  it("takes the keystroke away from the browser's own address-bar search", () => {
    renderPalette();

    const taken = fireEvent.keyDown(window, { key: "k", metaKey: true });
    // `fireEvent` returns false when a listener called `preventDefault`.
    expect(taken).toBe(false);
  });

  it("leaves Alt+K alone, and every other key", () => {
    renderPalette();

    fireEvent.keyDown(window, { key: "k", metaKey: true, altKey: true });
    fireEvent.keyDown(window, { key: "j", metaKey: true });
    expect(screen.queryByRole("combobox", { name: PALETTE_INPUT_LABEL })).not.toBeInTheDocument();
  });

  it("closes on Escape", async () => {
    const { user } = renderPalette();
    open();

    await user.keyboard("{Escape}");
    await gone();
  });

  it("forgets the query it was closed with", async () => {
    const { user } = renderPalette();

    open();
    await user.type(input(), "hopper");
    await user.keyboard("{Escape}");
    await gone();
    open();

    expect(input()).toHaveValue("");
  });
});

describe("what it offers", () => {
  it("lists the rail's own three places, and does not restate them", () => {
    renderPalette();
    open();

    for (const place of NAV) {
      expect(screen.getByRole("option", { name: new RegExp(place.label) })).toBeInTheDocument();
    }
    expect(NAV.map((p) => p.label)).toEqual(["CVs", "Skill Catalog", "Users"]);
  });

  it("lists every Agent Surface the dock's picker offers, in the picker's groups", () => {
    renderPalette();
    open();

    const names = optionNames();
    for (const group of SURFACE_GROUPS) {
      for (const s of group.surfaces) {
        expect(names).toContainEqual(expect.stringContaining(s.label));
        // The surface's own group travels with it, so `staffing` is findable by what it acts on.
        expect(names).toContainEqual(expect.stringContaining(group.category));
      }
    }
  });

  it("shows no people until there is something to search for", () => {
    renderPalette();
    open();

    expect(screen.queryByText("Grace Hopper")).not.toBeInTheDocument();
    expect(screen.queryByText("People")).not.toBeInTheDocument();
  });

  it("drops a heading whose group matched nothing", async () => {
    const { user } = renderPalette();
    open();

    await user.type(input(), "hopper");
    expect(screen.getByText("People")).toBeInTheDocument();
    expect(screen.queryByText("Places")).not.toBeInTheDocument();
    expect(screen.queryByText("Agent surfaces")).not.toBeInTheDocument();
  });

  it("says so plainly when nothing matches", async () => {
    const { user } = renderPalette();
    open();

    await user.type(input(), "zzzz");
    expect(screen.getByText(/No matches for/)).toHaveTextContent("zzzz");
  });
});

describe("finding a person", () => {
  it("matches on the name, in either order and part of a word", async () => {
    const { user } = renderPalette();
    open();

    await user.type(input(), "hop grac");
    expect(optionNames()).toEqual([expect.stringContaining("Grace Hopper")]);
  });

  /**
   * A paused Expert took themselves off the bench (P1T-185). The roster page keeps them, marked,
   * because that page is where staff account for who is on the bench; the palette is a
   * jump-to-a-person surface, and offering somebody here is offering them for work.
   */
  it("does not offer somebody who paused themselves", async () => {
    roster = [
      person({ id: "e1", firstName: "Grace", lastName: "Hopper" }),
      person({ id: "e2", firstName: "Paused", lastName: "Hopper", hiddenAt: "2026-09-01T10:00:00Z" }),
    ];

    const { user } = renderPalette();
    open();

    await user.type(input(), "hopper");
    expect(optionNames()).toEqual([expect.stringContaining("Grace Hopper")]);
  });

  it("matches on the title, the location and the email as well as the name", async () => {
    const { user } = renderPalette();
    open();

    await user.type(input(), "bletchley");
    expect(optionNames()).toEqual([expect.stringContaining("Alan Turing")]);

    await user.clear(input());
    await user.type(input(), "cryptanalyst");
    expect(optionNames()).toEqual([expect.stringContaining("Alan Turing")]);

    await user.clear(input());
    await user.type(input(), "grace@navy");
    expect(optionNames()).toEqual([expect.stringContaining("Grace Hopper")]);
  });

  it("jumps to the person and puts itself away", async () => {
    const { user } = renderPalette();
    open();

    await user.type(input(), "hopper");
    await user.click(screen.getByRole("option", { name: /Grace Hopper/ }));

    expect(where()).toBe("/experts/e1");
    await gone();
  });

  it("caps the list and says how much it left out rather than truncating in silence", async () => {
    roster = Array.from({ length: PEOPLE_SHOWN + 4 }, (_, i) =>
      person({ id: `e${i}`, firstName: "Ada", lastName: `Number${i}` }),
    );
    const { user } = renderPalette();
    open();

    await user.type(input(), "ada");
    expect(options()).toHaveLength(PEOPLE_SHOWN);
    expect(screen.getByText(`Showing ${PEOPLE_SHOWN} of ${PEOPLE_SHOWN + 4} matches — keep typing`))
      .toBeInTheDocument();
  });

  it("says the roster is still arriving rather than that nobody matched", async () => {
    roster = [];
    rosterLoading = true;
    const { user } = renderPalette();
    open();

    await user.type(input(), "hopper");
    expect(screen.getByText("Loading the roster…")).toBeInTheDocument();
    expect(screen.queryByText(/No matches for/)).not.toBeInTheDocument();
  });
});

describe("the keyboard path", () => {
  it("moves the highlight with the arrows and runs it with Enter, without leaving the input", async () => {
    const { user } = renderPalette();
    open();

    // The first row is highlighted from the start, so Enter alone is always a complete gesture.
    expect(options()[0]).toHaveAttribute("aria-selected", "true");
    expect(input()).toHaveAttribute("aria-activedescendant", options()[0].id);

    await user.keyboard("{ArrowDown}{ArrowDown}");
    expect(options()[2]).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{ArrowUp}");
    expect(options()[1]).toHaveAttribute("aria-selected", "true");
    expect(document.activeElement).toBe(input());

    await user.keyboard("{Enter}");
    expect(where()).toBe("/catalog");
  });

  it("wraps at both ends — a palette has no dead end", async () => {
    const { user } = renderPalette();
    open();

    await user.keyboard("{ArrowUp}");
    expect(options().at(-1)).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{ArrowDown}");
    expect(options()[0]).toHaveAttribute("aria-selected", "true");
  });

  it("puts the highlight back on the first row when the results change under it", async () => {
    const { user } = renderPalette();
    open();

    await user.keyboard("{End}");
    expect(options().at(-1)).toHaveAttribute("aria-selected", "true");

    await user.type(input(), "hopper");
    expect(options()[0]).toHaveAttribute("aria-selected", "true");
    expect(options()[0]).toHaveTextContent("Grace Hopper");
  });

  it("announces its results as a listbox, and its headings as neither", () => {
    renderPalette();
    open();

    const surfaces = SURFACE_GROUPS.flatMap((g) => g.surfaces).length;
    const listbox = screen.getByRole("listbox", { name: "Results" });
    expect(within(listbox).getAllByRole("option").length).toBe(NAV.length + surfaces);
  });
});

describe("jumping to an agent surface", () => {
  it("sends the request and opens a closed dock", async () => {
    const dock = dockWith({ open: false });
    const seen: string[] = [];
    function Listener() {
      useAgentSurfaceRequest((s) => seen.push(s));
      return null;
    }
    render(<Listener />);

    const { user } = renderPalette(dock);
    open();
    await user.click(screen.getByRole("option", { name: /Interview kit/ }));

    expect(seen).toEqual(["interview-kit"]);
    expect(dock.toggleOpen).toHaveBeenCalledOnce();
  });

  it("leaves an already-open dock open — the toggle is guarded, not fired", async () => {
    const dock = dockWith({ open: true });
    const { user } = renderPalette(dock);
    open();

    await user.click(screen.getByRole("option", { name: /Shortlist/ }));
    expect(dock.toggleOpen).not.toHaveBeenCalled();
  });

  it("reaches a real dock: the panel is showing the requested surface (P1T-152's picker agrees)", async () => {
    const user = userEvent.setup();
    render(
      <ThemeProvider theme={lightTheme}>
        <MemoryRouter>
          <AgentWidget dock={dockWith({ open: true })} />
          <CommandPalette dock={dockWith({ open: true })} />
        </MemoryRouter>
      </ThemeProvider>,
    );

    expect(currentAgentSurface()).toContain("Roster Q&A");

    open();
    await user.click(screen.getByRole("option", { name: /Interview kit/ }));

    await gone();
    expect(currentAgentSurface()).toContain("Interview kit");
  });

  it("ignores a name the dock does not know, instead of blanking the panel", () => {
    render(
      <ThemeProvider theme={lightTheme}>
        <MemoryRouter>
          <AgentWidget dock={dockWith({ open: true })} />
        </MemoryRouter>
      </ThemeProvider>,
    );

    act(() => requestAgentSurface("no-such-surface"));
    expect(currentAgentSurface()).toContain("Roster Q&A");
  });
});
