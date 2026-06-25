import { useCallback, useState } from "react";

// Persisted layout for the agent widget. `open` is session-only (starts closed); the dock mode and
// width are remembered so the widget reopens the way the user left it.
const DOCKED_KEY = "em.agent.docked";
const WIDTH_KEY = "em.agent.width";

export const DOCK_MIN_WIDTH = 360;
export const DOCK_DEFAULT_WIDTH = 420;

/** Max dock width = half the viewport, but never below the minimum. */
export function maxDockWidth(): number {
  return Math.max(DOCK_MIN_WIDTH, Math.round(window.innerWidth * 0.5));
}

export interface AgentDock {
  open: boolean;
  docked: boolean;
  width: number;
  toggleOpen: () => void;
  close: () => void;
  setDocked: (v: boolean) => void;
  setWidth: (v: number) => void;
}

export function useAgentDock(): AgentDock {
  const [open, setOpen] = useState(false);
  const [docked, setDockedState] = useState(() => localStorage.getItem(DOCKED_KEY) === "true");
  const [width, setWidthState] = useState(() => {
    const stored = Number(localStorage.getItem(WIDTH_KEY));
    return Number.isFinite(stored) && stored >= DOCK_MIN_WIDTH ? stored : DOCK_DEFAULT_WIDTH;
  });

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

  return { open, docked, width, toggleOpen, close, setDocked, setWidth };
}
