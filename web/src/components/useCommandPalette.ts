import { useEffect, useSyncExternalStore } from "react";

// The Command Palette's open state and the hotkey that opens it (P1T-165).
//
// A module store with a listener set, the same shape as `src/theme/mode.ts` and
// `src/auth/session.ts`: no React Context is added to this app (`manuals/spa-design-system.md` §2).
// It is a store rather than a prop for one concrete reason — the *trigger* lives in the rail and the
// *palette* is mounted beside the dock, so a prop would have to travel through `App` into two
// unrelated subtrees and would widen `AppRailNav`'s props for a button.
//
// Nothing here is persisted. The dock remembers how it was left because its width and its docking
// are a preference; a palette that reopened itself on reload would be a surprise instead.

let open = false;

const listeners = new Set<() => void>();

function notify(): void {
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function isCommandPaletteOpen(): boolean {
  return open;
}

export function openCommandPalette(): void {
  if (open) return;
  open = true;
  notify();
}

export function closeCommandPalette(): void {
  if (!open) return;
  open = false;
  notify();
}

export function toggleCommandPalette(): void {
  open = !open;
  notify();
}

/** Whether the palette is showing, reactively. */
export function useCommandPaletteOpen(): boolean {
  return useSyncExternalStore(subscribe, isCommandPaletteOpen, () => false);
}

/** The rail's trigger. Frozen alongside the other shell names — `manuals/spa-design-system.md` §9. */
export const PALETTE_TRIGGER_LABEL = "Search";

/**
 * The palette's own input. Long on purpose: it is both the placeholder and the accessible name, so
 * the three kinds of thing the palette can find are stated where a person is about to type rather
 * than only in a manual.
 */
export const PALETTE_INPUT_LABEL = "Jump to a place, a person, or an agent surface";

/** Whether this browser calls the modifier ⌘. Not a feature test — it is only what the hint says. */
function isApplePlatform(): boolean {
  return /Mac|iPhone|iPad|iPod/.test(navigator.userAgent);
}

/** The shortcut as this platform writes it, for the hint beside the rail's trigger. */
export function paletteHotkeyHint(): string {
  return isApplePlatform() ? "⌘K" : "Ctrl K";
}

/**
 * Whether a keystroke is the palette's.
 *
 * Both modifiers on every platform rather than the platform's own: a person who learned ⌃K
 * elsewhere is not wrong, and neither combination means anything else in this app. `metaKey` is
 * checked before `ctrlKey` for no reason other than reading order — either one opens it.
 */
export function isPaletteHotkey(e: KeyboardEvent): boolean {
  return (e.metaKey || e.ctrlKey) && !e.altKey && e.key.toLowerCase() === "k";
}

/**
 * Bind the hotkey for as long as the palette is mounted — which is exactly as long as there is a
 * signed-in shell to navigate (`App` mounts it beside the dock).
 *
 * `preventDefault` matters here: both Chrome and Firefox bind ⌘K/Ctrl+K to their own address-bar
 * search, so without it the browser takes the keystroke and the palette opens behind a focused
 * omnibox. It toggles rather than opens, so the same keystroke that opened it puts it away.
 */
export function useCommandPaletteHotkey(): void {
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (!isPaletteHotkey(e)) return;
      e.preventDefault();
      toggleCommandPalette();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);
}
