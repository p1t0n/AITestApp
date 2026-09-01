import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, renderHook, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@mui/material";
import { MemoryRouter } from "react-router-dom";
import AppRailNav from "./AppRail";
import {
  RAIL_COLLAPSED_WIDTH,
  RAIL_PUSH_VAR,
  RAIL_WIDTH,
  useAppRail,
  type AppRail,
} from "./useAppRail";
import {
  PALETTE_TRIGGER_LABEL,
  closeCommandPalette,
  isCommandPaletteOpen,
  paletteHotkeyHint,
} from "./useCommandPalette";
import { lightTheme } from "../theme";
import { getModeOverride } from "../theme/mode";
import { getToken, setSession } from "../auth/session";

// The rail takes its whole layout as a prop, so every state — collapsed, squeezed, narrow — is
// rendered here directly rather than through a mocked `matchMedia`. `useAppRail` is what owns the
// media queries, and it is tested on its own below.

function railWith(over: Partial<AppRail> = {}): AppRail {
  return {
    collapsed: false,
    squeezed: false,
    isNarrow: false,
    drawerOpen: false,
    width: RAIL_WIDTH,
    toggleCollapsed: vi.fn(),
    openDrawer: vi.fn(),
    closeDrawer: vi.fn(),
    ...over,
  };
}

function renderRail(rail: AppRail, path = "/") {
  return render(
    <ThemeProvider theme={lightTheme}>
      <MemoryRouter initialEntries={[path]}>
        <AppRailNav rail={rail} />
      </MemoryRouter>
    </ThemeProvider>,
  );
}

beforeEach(() => localStorage.clear());
afterEach(() => {
  localStorage.clear();
  document.documentElement.style.removeProperty(RAIL_PUSH_VAR);
});

describe("the rail, expanded", () => {
  it("carries the brand and the three places", () => {
    renderRail(railWith());

    expect(screen.getByText("ExpertToJob")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "CVs" })).toHaveAttribute("href", "/");
    expect(screen.getByRole("link", { name: "Skill Catalog" })).toHaveAttribute("href", "/catalog");
    expect(screen.getByRole("link", { name: "Users" })).toHaveAttribute("href", "/users");
  });

  it("keeps `Sign out` a button by that name — the e2e suite asserts exactly this", () => {
    renderRail(railWith());

    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  it("marks the current place, and marks only it", () => {
    renderRail(railWith(), "/catalog");

    expect(screen.getByRole("link", { name: "Skill Catalog" })).toHaveClass("Mui-selected");
    expect(screen.getByRole("link", { name: "CVs" })).not.toHaveClass("Mui-selected");
  });

  it("does not treat every path as the roster: `/` matches exactly", () => {
    renderRail(railWith(), "/experts/abc");

    expect(screen.getByRole("link", { name: "CVs" })).not.toHaveClass("Mui-selected");
  });

  it("shows who is signed in", () => {
    setSession("t", "ada@example.com");

    renderRail(railWith());

    expect(screen.getByTestId("rail-user")).toHaveTextContent("ada@example.com");
  });

  it("renders no user block for a session stored before the email was kept", () => {
    setSession("t");

    renderRail(railWith());

    expect(screen.queryByTestId("rail-user")).not.toBeInTheDocument();
  });

  it("signs out and leaves for the sign-in page", async () => {
    const user = userEvent.setup();
    setSession("t", "ada@example.com");
    renderRail(railWith());

    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(getToken()).toBeNull();
  });
});

describe("the rail's palette trigger (P1T-165)", () => {
  it("opens the palette, and shows the shortcut that does the same thing", async () => {
    const user = userEvent.setup();
    renderRail(railWith());

    const trigger = screen.getByRole("button", { name: PALETTE_TRIGGER_LABEL });
    // The hint is decoration for a row that already carries its own name, so it must not be part
    // of it — `Search ⌘K` would be a third accessible name to keep in step with the e2e suite.
    expect(trigger).toHaveTextContent(paletteHotkeyHint());
    expect(trigger).toHaveAccessibleName(PALETTE_TRIGGER_LABEL);

    await user.click(trigger);
    expect(isCommandPaletteOpen()).toBe(true);
    closeCommandPalette();
  });

  it("keeps its name with the labels gone, and drops the hint with them", () => {
    renderRail(railWith({ collapsed: true, width: RAIL_COLLAPSED_WIDTH }));

    const trigger = screen.getByRole("button", { name: PALETTE_TRIGGER_LABEL });
    expect(trigger).toBeInTheDocument();
    expect(trigger).not.toHaveTextContent(paletteHotkeyHint());
  });

  it("closes the mobile drawer on its way, like every other row that leaves", async () => {
    const user = userEvent.setup();
    const closeDrawer = vi.fn();
    renderRail(railWith({ isNarrow: true, drawerOpen: true, width: 0, closeDrawer }));

    await user.click(screen.getByRole("button", { name: PALETTE_TRIGGER_LABEL }));

    expect(closeDrawer).toHaveBeenCalledOnce();
    closeCommandPalette();
  });
});

describe("the rail, collapsed", () => {
  it("keeps every accessible name when the labels are gone", () => {
    renderRail(railWith({ collapsed: true, width: RAIL_COLLAPSED_WIDTH }));

    // The whole point of the `aria-label`: an icon with no name is a broken test, not a style
    // choice (`manuals/spa-design-system.md` §9).
    expect(screen.getByRole("link", { name: "CVs" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Skill Catalog" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Users" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Theme" })).toBeInTheDocument();
  });

  it("offers to expand rather than to collapse", () => {
    renderRail(railWith({ collapsed: true, width: RAIL_COLLAPSED_WIDTH }));

    expect(screen.getByRole("button", { name: "Expand the navigation rail" })).toBeEnabled();
    expect(screen.queryByRole("button", { name: "Collapse the navigation rail" })).toBeNull();
  });

  it("publishes the collapsed width, so the shell closes the gap it left", () => {
    renderRail(railWith({ collapsed: true, width: RAIL_COLLAPSED_WIDTH }));

    expect(document.documentElement.style.getPropertyValue(RAIL_PUSH_VAR)).toBe("64px");
  });
});

describe("the rail, squeezed by the dock", () => {
  it("disables the expand control instead of letting it do nothing", () => {
    renderRail(railWith({ collapsed: true, squeezed: true, width: RAIL_COLLAPSED_WIDTH }));

    // A control that looks like it worked and changed nothing is worse than one that plainly
    // cannot — the same call as the disabled skill picker in P1T-156.
    expect(screen.getByRole("button", { name: "Expand the navigation rail" })).toBeDisabled();
  });
});

describe("the rail, below md", () => {
  it("becomes a slim top bar with the rail behind it, covering nothing", () => {
    renderRail(railWith({ isNarrow: true, width: 0 }));

    expect(screen.getByRole("button", { name: "Open the navigation" })).toBeInTheDocument();
    expect(document.documentElement.style.getPropertyValue(RAIL_PUSH_VAR)).toBe("0px");
    // The drawer is closed, so nothing inside it is reachable yet.
    expect(screen.queryByRole("link", { name: "Users" })).toBeNull();
  });

  it("opens the drawer on the menu button", async () => {
    const user = userEvent.setup();
    const rail = railWith({ isNarrow: true, width: 0 });
    renderRail(rail);

    await user.click(screen.getByRole("button", { name: "Open the navigation" }));

    expect(rail.openDrawer).toHaveBeenCalled();
  });

  it("shows full labels in the drawer — an overlay costs the content nothing", async () => {
    const user = userEvent.setup();
    const rail = railWith({ isNarrow: true, drawerOpen: true, width: 0 });
    renderRail(rail);

    const drawer = screen.getByRole("presentation");
    expect(within(drawer).getByRole("link", { name: "Skill Catalog" })).toBeInTheDocument();
    // …and there is no collapse control in a drawer: collapsing an overlay buys nothing.
    expect(within(drawer).queryByRole("button", { name: /navigation rail/ })).toBeNull();

    await user.click(within(drawer).getByRole("link", { name: "Users" }));
    expect(rail.closeDrawer).toHaveBeenCalled();
  });
});

describe("the theme control", () => {
  it("offers three choices, because the mechanism underneath has three states", async () => {
    const user = userEvent.setup();
    renderRail(railWith());

    await user.click(screen.getByRole("button", { name: "Theme" }));

    expect(await screen.findByRole("menuitem", { name: "Light" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Dark" })).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "System" })).toBeInTheDocument();
    // Nothing is pinned yet, so the app is still following the OS.
    expect(screen.getByRole("menuitem", { name: "System" })).toHaveClass("Mui-selected");
  });

  it("pins a mode, and the choice survives a reload", async () => {
    const user = userEvent.setup();
    const { unmount } = renderRail(railWith());

    await user.click(screen.getByRole("button", { name: "Theme" }));
    await user.click(await screen.findByRole("menuitem", { name: "Dark" }));

    expect(getModeOverride()).toBe("dark");

    // A reload is a fresh mount reading the same storage.
    unmount();
    renderRail(railWith());
    await user.click(screen.getByRole("button", { name: "Theme" }));
    expect(await screen.findByRole("menuitem", { name: "Dark" })).toHaveClass("Mui-selected");
  });

  it("hands the default back — `System` is reachable after a mode was pinned", async () => {
    const user = userEvent.setup();
    renderRail(railWith());

    await user.click(screen.getByRole("button", { name: "Theme" }));
    await user.click(await screen.findByRole("menuitem", { name: "Light" }));
    expect(getModeOverride()).toBe("light");

    await user.click(screen.getByRole("button", { name: "Theme" }));
    await user.click(await screen.findByRole("menuitem", { name: "System" }));

    // The override is *removed*, not set to whatever the OS currently says: "no value" is what
    // means "still following the OS" (`src/theme/mode.ts`).
    expect(getModeOverride()).toBeNull();
  });
});

describe("useAppRail", () => {
  // jsdom implements no `matchMedia`, so MUI's `useMediaQuery` answers false for both queries —
  // which is the wide, dock-closed viewport, and exactly the default this app opens on.
  it("starts expanded and pushes the full width", () => {
    const { result } = renderHook(() => useAppRail(0));

    expect(result.current.collapsed).toBe(false);
    expect(result.current.width).toBe(RAIL_WIDTH);
    expect(result.current.isNarrow).toBe(false);
  });

  it("remembers a collapse across a remount", () => {
    const first = renderHook(() => useAppRail(0));
    act(() => first.result.current.toggleCollapsed());
    expect(first.result.current.collapsed).toBe(true);
    expect(first.result.current.width).toBe(RAIL_COLLAPSED_WIDTH);
    first.unmount();

    const second = renderHook(() => useAppRail(0));

    expect(second.result.current.collapsed).toBe(true);
  });

  it("keeps the drawer session-only: it starts closed however the rail was left", () => {
    localStorage.setItem("em.rail.collapsed", "true");

    const { result } = renderHook(() => useAppRail(0));

    expect(result.current.drawerOpen).toBe(false);
    act(() => result.current.openDrawer());
    expect(result.current.drawerOpen).toBe(true);
  });
});
