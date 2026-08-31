import { useEffect, useRef } from "react";
import type { AgentDock } from "../useAgentDock";

// A one-way channel for "show this Agent Surface", from anywhere in the app to whichever dock is
// mounted (P1T-165). The ⌘K palette is its only caller today.
//
// Deliberately *not* a field on `AgentDock`. That interface is built as an object literal in eleven
// test files, so widening it costs an edit in every one of them — the same price that keeps the
// dock's state hoisted into `App` rather than making the widget uncontrolled
// (`manuals/spa-architecture.md` §13). A message with no state to hold does not justify it.
//
// A Surface Request is an event, not a value: it is handed to the listener synchronously and
// nothing remembers it afterwards. So there is no pending request to go stale, no second source of
// truth for which surface is showing — the widget's own `useState` stays the only one — and a
// request that arrives while no dock is mounted (signed out) is simply dropped.

type SurfaceListener = (surface: string) => void;

const listeners = new Set<SurfaceListener>();

/**
 * Ask the mounted dock to show a surface, by the name the picker uses for it.
 *
 * A plain string rather than the widget's own `Surface` union: this module carries the message and
 * has no business knowing the vocabulary. The widget validates the name and ignores one it does not
 * recognise, which is also what keeps a stale caller from wedging the dock on nothing.
 */
export function requestAgentSurface(surface: string): void {
  for (const listener of listeners) listener(surface);
}

/** Honour Surface Requests for as long as the component is mounted. The dock is the only caller. */
export function useAgentSurfaceRequest(onRequest: SurfaceListener): void {
  // The listener is registered once and reads the latest callback through a ref, so a request is
  // never delivered to a closure over last render's state.
  const latest = useRef(onRequest);
  useEffect(() => {
    latest.current = onRequest;
  });

  useEffect(() => {
    const listener: SurfaceListener = (surface) => latest.current(surface);
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  }, []);
}

/**
 * Open the dock on a named surface: the request first, then the dock, so the panel that appears is
 * already the requested one rather than the last one and then a flicker.
 *
 * `toggleOpen` is guarded rather than replaced by an `open()` for the same reason the request is not
 * a dock field — the existing interface can already express this, and widening it cannot.
 */
export function openAgentSurface(dock: AgentDock, surface: string): void {
  requestAgentSurface(surface);
  if (!dock.open) dock.toggleOpen();
}
