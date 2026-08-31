import { useCallback, useEffect, useState } from "react";
import useMediaQuery from "@mui/material/useMediaQuery";

// Persisted layout for the agent widget. `open` is session-only (starts closed); the dock mode and
// width are remembered so the widget reopens the way the user left it.
const DOCKED_KEY = "em.agent.docked";
const WIDTH_KEY = "em.agent.width";

export const DOCK_MIN_WIDTH = 360;
export const DOCK_DEFAULT_WIDTH = 420;

/** Below this the dock has no room to sit beside the app, so it takes the whole viewport. */
export const DOCK_NARROW_QUERY = "(max-width:600px)";

/**
 * How much horizontal space the dock is currently covering, published on the document root by
 * {@link useDockPush}.
 *
 * The dock is `position: fixed`, so it takes no part in layout and a docked sidebar would sit on
 * top of the app. Whoever owns the page container pads by this variable — `var(--agent-dock-push,
 * 0px)` — without knowing whether the dock is open, docked, or how wide it is. That is the whole
 * point: the width travels, the state does not.
 */
export const DOCK_PUSH_VAR = "--agent-dock-push";

export interface AgentDock {
  open: boolean;
  docked: boolean;
  width: number;
  isNarrow: boolean;
  toggleOpen: () => void;
  close: () => void;
  setDocked: (v: boolean) => void;
  setWidth: (v: number) => void;
}

/** Max dock width = half the viewport, but never below the minimum. */
export function maxDockWidth(): number {
  return Math.max(DOCK_MIN_WIDTH, Math.round(window.innerWidth * 0.5));
}

export function useAgentDock(): AgentDock {
  const [open, setOpen] = useState(false);
  const [docked, setDockedState] = useState(() => localStorage.getItem(DOCKED_KEY) === "true");
  const [width, setWidthState] = useState(() => {
    const stored = Number(localStorage.getItem(WIDTH_KEY));
    return Number.isFinite(stored) && stored >= DOCK_MIN_WIDTH ? stored : DOCK_DEFAULT_WIDTH;
  });
  const isNarrow = useMediaQuery(DOCK_NARROW_QUERY);

  const setDocked = useCallback((v: boolean) => {
    setDockedState(v);
    localStorage.setItem(DOCKED_KEY, String(v));
  }, []);

  const setWidth = useCallback((v: number) => {
    const clamped = Math.min(maxDockWidth(), Math.max(DOCK_MIN_WIDTH, v));
    setWidthState(clamped);
    localStorage.setItem(WIDTH_KEY, String(Math.round(clamped)));
  }, []);

  const toggleOpen = useCallback(() => setOpen((o) => !o), []);
  const close = useCallback(() => setOpen(false), []);

  return { open, docked, width, isNarrow, toggleOpen, close, setDocked, setWidth };
}

/**
 * How much of the viewport the dock is covering right now.
 *
 * A docked sidebar pushes the app left. A floating bubble and a narrow full-width overlay both sit
 * over the app on purpose, so they cover nothing as far as layout is concerned.
 *
 * Exported because one other thing needs the answer and must not re-derive it: the rail's squeeze
 * rule (`railSqueezeQuery`) is a function of what the *other* edge is covering. The shell is the
 * only place that knows about both edges, and this keeps the expression itself in one file.
 */
export function dockPushWidth(dock: AgentDock): number {
  return dock.open && dock.docked && !dock.isNarrow ? dock.width : 0;
}

/**
 * Publishes {@link DOCK_PUSH_VAR} for as long as the dock is mounted. Called by the dock, so the
 * property exists exactly while there is a dock to make room for: unmounting it (signing out)
 * removes the property and every container's `var(…, 0px)` fallback closes the gap on its own.
 */
export function useDockPush(dock: AgentDock): void {
  const push = dockPushWidth(dock);

  useEffect(() => {
    const root = document.documentElement;
    root.style.setProperty(DOCK_PUSH_VAR, `${push}px`);
    return () => {
      root.style.removeProperty(DOCK_PUSH_VAR);
    };
  }, [push]);
}
