// The dock's chrome (P1T-163) — slice 5 of the design-system chain.
//
// Everything here is about the *panel*, never about what a panel shows: the dock's ten other spec
// files own the surfaces themselves and are the tightest net in this repo, so a red one of those
// means this slice reached further than intended.
//
// Asserted on resolved colour and on real DOM, not on the emitted `sx`, for the reason slice 1
// learned the hard way: a focus ring that emits perfect CSS and renders nothing passes every
// configuration check there is (`manuals/spa-design-system.md` §8). jsdom implements no layout and
// no media queries, so "does the header wrap at 360px" is not answerable here and is a screenshot;
// "what colour is this, and can a keyboard reach it" are, and those are the two claims that were
// silently wrong before this slice.
import { describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { CssBaseline, ThemeProvider } from "@mui/material";
import AgentWidget, { RESIZE_HANDLE_LABEL, RESIZE_STEP } from "./AgentWidget";
import { AgentMarkdown } from "./agent/AgentMarkdown";
import { DOCK_MIN_WIDTH, maxDockWidth, type AgentDock } from "./useAgentDock";
import { darkTheme, lightTheme } from "../theme";
import { tokens } from "../theme/tokens";
import { contrastRatio, rgb, rgbaOver } from "../test/contrast";
import { selectAgentSurface } from "../test/agentSurface";

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  // Declared inside the factory: `vi.mock` is hoisted above every top-level binding in the file.
  const idle = () => ({ mutateAsync: vi.fn(), mutate: vi.fn(), isPending: false, isSuccess: false, isError: false, error: null });
  return {
    ...actual,
    useUsage: () => ({ data: undefined, isLoading: false, isError: false, error: null }),
    useExperts: () => ({ data: [], isLoading: false }),
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

const dark = tokens.modes.dark;

function makeDock(over: Partial<AgentDock> = {}): AgentDock & { setWidth: ReturnType<typeof vi.fn> } {
  const setWidth = vi.fn();
  return {
    open: true,
    docked: true,
    width: 420,
    isNarrow: false,
    toggleOpen: vi.fn(),
    close: vi.fn(),
    setDocked: vi.fn(),
    // A spy rather than the hook: the resize claims are about which number the dock is *asked*
    // for, since the clamping itself belongs to the hook and has its own spec
    // (`useAgentDock.test.tsx`).
    setWidth,
    ...over,
  } as AgentDock & { setWidth: ReturnType<typeof vi.fn> };
}

/** The dock in the dark theme, which is the mode this slice's colour claims are about. */
function renderDock(dock: AgentDock, theme = darkTheme) {
  const user = userEvent.setup();
  render(
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <MemoryRouter>
        <AgentWidget dock={dock} />
      </MemoryRouter>
    </ThemeProvider>,
  );
  return user;
}

/** The one bar: the element carrying the title, the three controls and the picker under them. */
function headerBar(): HTMLElement {
  return screen.getByText("Agents").closest("div")!.parentElement!;
}

describe("the dock header is one bar, in the app's own surfaces", () => {
  it("drops the accent slab for the raised step of the surface ramp", () => {
    renderDock(makeDock());

    // The whole reason the dock read as bolted on: a solid `primary.main` header, in an app whose
    // accent is reserved for the primary action and the focus ring (§3). It is chrome now.
    const bar = headerBar();
    expect(getComputedStyle(bar).backgroundColor).toBe(rgb(dark.surface.raised));
    expect(getComputedStyle(bar).backgroundColor).not.toBe(rgb(dark.primary.main));
    expect(getComputedStyle(bar).borderBottomColor).toBe(rgb(dark.divider));
  });

  it("keeps its title legible on that surface, and keeps it a title", () => {
    renderDock(makeDock());
    const title = screen.getByText("Agents");

    expect(getComputedStyle(title).color).toBe(rgb(dark.text.primary));
    expect(contrastRatio(dark.text.primary, dark.surface.raised)).toBeGreaterThanOrEqual(4.5);
    // `noWrap` is what holds the "does not wrap or clip at 360px" claim in the one direction jsdom
    // can answer: the elastic element is the title, so the controls never get pushed to a new line.
    expect(getComputedStyle(title).whiteSpace).toBe("nowrap");
  });

  it("names every control in the bar, including the one that had no name at all", () => {
    renderDock(makeDock());

    // Close was an icon with no tooltip and no label: the dock's own exit was unreachable by name
    // for a screen reader, and untestable by role for everyone else.
    expect(screen.getByRole("button", { name: "Close the agents assistant" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Token usage" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Float" })).toBeInTheDocument();
    // All three live in the same row as the title — one bar, not three loose buttons.
    expect(within(headerBar()).getByRole("button", { name: "Close the agents assistant" })).toBeInTheDocument();
  });

  it("closes the dock from that control", async () => {
    const dock = makeDock();
    const user = renderDock(dock);

    await user.click(screen.getByRole("button", { name: "Close the agents assistant" }));
    expect(dock.close).toHaveBeenCalled();
  });

  it("marks the ledger peek with the accent only while it is the thing on screen", async () => {
    const user = renderDock(makeDock());
    const ledger = screen.getByRole("button", { name: "Token usage" });

    expect(getComputedStyle(ledger).color).toBe(rgb(dark.text.primary));

    await user.click(ledger);
    expect(getComputedStyle(screen.getByRole("button", { name: "Token usage" })).color).toBe(
      rgb(dark.primary.main),
    );
  });

  it("puts the picker inside the same bar, still named the way the specs reach it", () => {
    renderDock(makeDock());
    const picker = screen.getByRole("button", { name: /^Agent surface: / });

    expect(within(headerBar()).getByRole("button", { name: /^Agent surface: / })).toBe(picker);
    expect(picker).toHaveTextContent("Roster Q&A");
    // A bordered control on the raised step: a borderless label there reads as a heading.
    expect(getComputedStyle(picker).borderColor).toBe(rgb(dark.divider));
  });
});

describe("the resize handle is a control now, not a hover-only strip", () => {
  it("exists only where resizing does — docked and wide", () => {
    renderDock(makeDock());
    expect(screen.getByRole("separator", { name: RESIZE_HANDLE_LABEL })).toBeInTheDocument();

    cleanup();
    renderDock(makeDock({ docked: false }));
    expect(screen.queryByRole("separator", { name: RESIZE_HANDLE_LABEL })).not.toBeInTheDocument();

    cleanup();
    renderDock(makeDock({ isNarrow: true }));
    expect(screen.queryByRole("separator", { name: RESIZE_HANDLE_LABEL })).not.toBeInTheDocument();
  });

  it("announces the width it is holding, and the range it may move in", () => {
    renderDock(makeDock({ width: 460 }));
    const handle = screen.getByRole("separator", { name: RESIZE_HANDLE_LABEL });

    expect(handle).toHaveAttribute("aria-orientation", "vertical");
    expect(handle).toHaveAttribute("aria-valuenow", "460");
    expect(handle).toHaveAttribute("aria-valuemin", String(DOCK_MIN_WIDTH));
    expect(handle).toHaveAttribute("aria-valuemax", String(Math.round(maxDockWidth())));
  });

  it("is reachable by keyboard at all, which is the whole defect", async () => {
    const user = renderDock(makeDock());
    const handle = screen.getByRole("separator", { name: RESIZE_HANDLE_LABEL });

    expect(handle).toHaveAttribute("tabindex", "0");
    handle.focus();
    expect(handle).toHaveFocus();
    // And it acts on the keys it just took focus for.
    await user.keyboard("{ArrowLeft}");
    expect(handle).toHaveFocus();
  });

  it("moves the edge with the arrows: left grows the dock, right shrinks it", async () => {
    const dock = makeDock({ width: 420 });
    const user = renderDock(dock);
    screen.getByRole("separator", { name: RESIZE_HANDLE_LABEL }).focus();

    await user.keyboard("{ArrowLeft}");
    expect(dock.setWidth).toHaveBeenLastCalledWith(420 + RESIZE_STEP);

    await user.keyboard("{ArrowRight}");
    expect(dock.setWidth).toHaveBeenLastCalledWith(420 - RESIZE_STEP);
  });

  it("takes bigger steps with Shift, and jumps to either end with Home and End", async () => {
    const dock = makeDock({ width: 420 });
    const user = renderDock(dock);
    screen.getByRole("separator", { name: RESIZE_HANDLE_LABEL }).focus();

    await user.keyboard("{Shift>}{ArrowLeft}{/Shift}");
    expect(dock.setWidth).toHaveBeenLastCalledWith(420 + RESIZE_STEP * 4);

    await user.keyboard("{Home}");
    expect(dock.setWidth).toHaveBeenLastCalledWith(DOCK_MIN_WIDTH);

    await user.keyboard("{End}");
    expect(dock.setWidth).toHaveBeenLastCalledWith(maxDockWidth());
  });

  it("leaves keys it does not own alone", async () => {
    const dock = makeDock();
    const user = renderDock(dock);
    screen.getByRole("separator", { name: RESIZE_HANDLE_LABEL }).focus();

    await user.keyboard("{ArrowUp}{Enter}a");
    expect(dock.setWidth).not.toHaveBeenCalled();
  });
});

describe("the transcript reads in dark mode", () => {
  /** Renders one message of each kind by driving the real Roster Q&A surface. */
  async function transcript() {
    const answer = "Ada knows **React**.";
    const ask = vi.fn().mockResolvedValue({ answer, threadId: "t-1" });
    const api = await import("../api");
    vi.spyOn(api, "useRosterQa").mockReturnValue({ mutateAsync: ask, isPending: false } as never);

    const user = renderDock(makeDock());
    await user.type(screen.getByPlaceholderText("Ask about the roster…"), "Who knows React?");
    await user.click(screen.getByLabelText("Send"));
    await screen.findByText(/Ada knows/);
    return user;
  }

  it("greets an empty panel with something placed rather than a sentence in the corner", () => {
    renderDock(makeDock());
    const hint = screen.getByText(/Who knows React and is available this summer/);

    // Same words — they are the useful part — on the panel's own secondary text role.
    expect(getComputedStyle(hint).color).toBe(rgb(dark.text.secondary));
    expect(contrastRatio(dark.text.secondary, dark.surface.surface)).toBeGreaterThanOrEqual(4.5);
  });

  it("washes the person's bubble with the accent instead of filling with it", async () => {
    await transcript();
    const bubble = screen.getByText("Who knows React?").closest(".MuiPaper-root")!;
    const style = getComputedStyle(bubble);

    // `action.selected` is an alpha role, so what it costs in contrast is a property of the
    // composite over the panel — never of the tint on its own.
    expect(style.backgroundColor).toBe(dark.action.selected);
    expect(style.backgroundColor).not.toBe(rgb(dark.primary.main));
    const composite = rgbaOver(style.backgroundColor, dark.surface.surface);
    expect(contrastRatio(dark.text.primary, composite)).toBeGreaterThanOrEqual(4.5);
    // And the accent still marks it, as an edge.
    expect(style.borderColor).toBe(rgb(dark.primary.main));
  });

  it("leaves the agent's bubble on the raised step", async () => {
    await transcript();
    const bubble = screen.getByText(/Ada knows/).closest(".MuiPaper-root")!;

    expect(getComputedStyle(bubble).backgroundColor).toBe(rgb(dark.surface.raised));
    expect(contrastRatio(dark.text.primary, dark.surface.raised)).toBeGreaterThanOrEqual(4.5);
  });
});

describe("a degradation notice is readable on the fill it chose", () => {
  it("labels the paused scan banner, which had the fill and not the label colour", async () => {
    const api = await import("../api");
    vi.spyOn(api, "useRosterScanJob").mockReturnValue({
      data: {
        jobId: "j-1",
        state: "paused",
        pauseReason: "quota",
        resumeAt: "2026-08-17T07:00:00Z",
        progress: { scored: 1, failed: 0, pending: 1, total: 2, settled: 1 },
        candidates: [],
      },
    } as never);

    const user = renderDock(makeDock());
    await selectAgentSurface(user, "Roster scan");

    // In dark mode this panel is `#FFD37A`, and it was inheriting the app's near-white
    // `text.primary` onto it — about 1.2:1 on the one notice that explains why a scan stopped.
    // `light` is a saturated mid-step in this palette, not a tint, so a fill of it always needs
    // its own label colour; the dock's four other warning wells already said so.
    const banner = screen.getByTestId("scan-paused");
    expect(getComputedStyle(banner).color).toBe(rgb(dark.warning.contrastText));
    expect(contrastRatio(dark.warning.contrastText, dark.warning.light)).toBeGreaterThanOrEqual(4.5);
  });
});

describe("a long agent answer stays inside a 360px panel", () => {
  function markdown(text: string) {
    const { container } = render(
      <ThemeProvider theme={darkTheme}>
        <MemoryRouter>
          <AgentMarkdown text={text} />
        </MemoryRouter>
      </ThemeProvider>,
    );
    return container;
  }

  it("gives a fenced block a well of its own and lets it scroll itself", () => {
    const container = markdown("```\nvery long line of code\n```");
    const pre = container.querySelector("pre")!;
    const code = pre.querySelector("code")!;

    expect(getComputedStyle(pre).backgroundColor).toBe(rgb(dark.surface.raised));
    expect(getComputedStyle(pre).overflowX).toBe("auto");
    // The inner `code` stops repainting the same fill on top of the block's own. jsdom spells
    // `transparent` as the rgba it computes to.
    expect(getComputedStyle(code).backgroundColor).toBe("rgba(0, 0, 0, 0)");
  });

  it("scrolls a wide table without taking the table's own role away", () => {
    const container = markdown("| a | b |\n| - | - |\n| 1 | 2 |");
    const table = screen.getByRole("table");

    // `display: block` is the usual fix for this and drops the role in Chrome — a scrollbar bought
    // by deleting the element's semantics is not a fix. The wrapper is what scrolls.
    expect(getComputedStyle(table).display).not.toBe("block");
    expect(getComputedStyle(table.parentElement!).overflowX).toBe("auto");
    expect(getComputedStyle(container.querySelector("th")!).backgroundColor).toBe(
      rgb(dark.surface.raised),
    );
  });

  it("breaks a linkified expert id rather than the panel", () => {
    const id = "11111111-2222-3333-4444-555555555555";
    const container = markdown(`See ${id} for the detail.`);

    expect(screen.getByRole("link", { name: id })).toHaveAttribute("href", `/experts/${id}`);
    expect(getComputedStyle(container.firstElementChild!).overflowWrap).toBe("anywhere");
  });

  it("renders code in the mono family the token layer has been carrying with no consumer", () => {
    const container = markdown("call `cv_get` first");
    const code = container.querySelector("code")!;

    // `fontFamilyMono` shipped in slice 1 with nothing pointing at it — the same gap slice 2 found
    // in `surface.outline`. It is a document-level type floor, so it comes from `CssBaseline`.
    render(
      <ThemeProvider theme={lightTheme}>
        <CssBaseline />
        <span />
      </ThemeProvider>,
    );
    expect(getComputedStyle(code).fontFamily).toBe(tokens.type.fontFamilyMono);
  });
});
