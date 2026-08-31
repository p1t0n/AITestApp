import { useCallback, useEffect, useState } from "react";
import useMediaQuery from "@mui/material/useMediaQuery";

// Persisted layout for the app's left rail. The collapse choice is remembered; the mobile drawer's
// open state is session-only (it starts closed, exactly like the dock's `open`).
//
// This module is deliberately the mirror image of `useAgentDock.ts`. The two edges of the shell —
// rail on the left, agent dock on the right — are both `position: fixed`, both publish how much of
// the viewport they cover as a CSS custom property, and neither takes part in layout. The dock's
// contract (P1T-154) is *copied* here, not modified: whoever owns the page container pads by both
// variables and knows nothing about either edge's state.
const COLLAPSED_KEY = "em.rail.collapsed";

/** The rail with its labels showing. */
export const RAIL_WIDTH = 240;

/** The rail as icons only. Wide enough for a 40px target with the same gutter either side. */
export const RAIL_COLLAPSED_WIDTH = 64;

/**
 * The narrowest routed content this shell will accept.
 *
 * This is the rule behind "the rail collapses before the content does". Both edges push, so on a
 * narrow-ish viewport with the dock docked open the middle column is what pays — and left to
 * itself it can be squeezed to nothing. Rather than discovering that at some viewport width, the
 * rail gives up its labels the moment an expanded rail would take the content below this floor.
 *
 * 720px is a two-column form plus its labels, and the roster table's own natural width; below it
 * the tables start to wrap in ways that read as broken rather than as dense.
 */
export const RAIL_CONTENT_FLOOR = 720;

/** Below `md` the rail has no room to sit beside the app, so it becomes a temporary drawer. */
export const RAIL_NARROW_QUERY = "(max-width:899.95px)";

/**
 * How much horizontal space the rail is currently covering, published on the document root by
 * {@link useRailPush}. The left-hand twin of `DOCK_PUSH_VAR` — see that constant for why the width
 * travels and the state does not.
 */
export const RAIL_PUSH_VAR = "--app-rail-push";

/**
 * The viewport widths at which an expanded rail would breach {@link RAIL_CONTENT_FLOOR}, given how
 * much the dock is currently covering.
 *
 * A query rather than a `window.innerWidth` read so the answer is subscribed to rather than
 * sampled, and so it re-subscribes on its own when the dock is resized: the whole rule is one
 * string, and it is the one thing to change if the floor moves.
 */
export function railSqueezeQuery(dockPush: number): string {
  return `(max-width:${RAIL_CONTENT_FLOOR + RAIL_WIDTH + dockPush - 0.05}px)`;
}

export interface AppRail {
  /** Icons only: either pinned that way, or forced by {@link AppRail.squeezed}. */
  collapsed: boolean;
  /** True while the viewport cannot afford an expanded rail. The collapse control says so. */
  squeezed: boolean;
  /** Below `md`: the rail is a temporary drawer behind a slim top bar. */
  isNarrow: boolean;
  drawerOpen: boolean;
  /** How much of the viewport the rail covers. Zero as a drawer, which overlays on purpose. */
  width: number;
  toggleCollapsed: () => void;
  openDrawer: () => void;
  closeDrawer: () => void;
}

export function useAppRail(dockPush: number): AppRail {
  const [pinnedCollapsed, setPinnedCollapsed] = useState(
    () => localStorage.getItem(COLLAPSED_KEY) === "true",
  );
  const [drawerOpen, setDrawerOpen] = useState(false);
  const isNarrow = useMediaQuery(RAIL_NARROW_QUERY);
  const squeezed = useMediaQuery(railSqueezeQuery(dockPush));

  // As a drawer the rail overlays the app, so it costs the content nothing and shows its labels
  // whatever the viewport says. The squeeze rule only governs the rail that pushes.
  const collapsed = !isNarrow && (pinnedCollapsed || squeezed);

  const toggleCollapsed = useCallback(() => {
    setPinnedCollapsed((c) => {
      const next = !c;
      localStorage.setItem(COLLAPSED_KEY, String(next));
      return next;
    });
  }, []);

  const openDrawer = useCallback(() => setDrawerOpen(true), []);
  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  return {
    collapsed,
    squeezed: squeezed && !isNarrow,
    isNarrow,
    drawerOpen,
    width: isNarrow ? 0 : collapsed ? RAIL_COLLAPSED_WIDTH : RAIL_WIDTH,
    toggleCollapsed,
    openDrawer,
    closeDrawer,
  };
}

/**
 * Publishes {@link RAIL_PUSH_VAR} for as long as the rail is mounted. Called by the rail, so the
 * property exists exactly while there is a rail to make room for: the auth pages render no rail,
 * and every container's `var(…, 0px)` fallback closes the gap on its own.
 */
export function useRailPush(rail: AppRail): void {
  const push = rail.width;

  useEffect(() => {
    const root = document.documentElement;
    root.style.setProperty(RAIL_PUSH_VAR, `${push}px`);
    return () => {
      root.style.removeProperty(RAIL_PUSH_VAR);
    };
  }, [push]);
}
