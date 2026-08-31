// Theme Mode selection. The OS preference is the default; a person's explicit choice overrides it
// and persists in localStorage. Deliberately the same shape as `src/auth/session.ts`: storage is
// the source of truth, a listener set notifies React, and the `storage` event carries a change
// between tabs. No React Context is added to this app — see `manuals/spa-design-system.md` §2.
//
// A Theme Mode is a preference of the browser, never of the account: it is not roster data and does
// not travel with the user (CONTEXT.md, "Theme Mode").
import { useSyncExternalStore } from "react";

export type ThemeMode = "light" | "dark";

const MODE_KEY = "em.theme.mode";
const DARK_QUERY = "(prefers-color-scheme: dark)";

const listeners = new Set<() => void>();

function notify(): void {
  for (const l of listeners) l();
}

function isMode(v: string | null): v is ThemeMode {
  return v === "light" || v === "dark";
}

/** The `prefers-color-scheme` media query, or `null` where the environment has no matchMedia. */
function darkQuery(): MediaQueryList | null {
  // jsdom implements no matchMedia at all, and this module is imported by component tests.
  return typeof window.matchMedia === "function" ? window.matchMedia(DARK_QUERY) : null;
}

/** What the operating system asks for. Light when it does not say — the safer default to read. */
export function getSystemMode(): ThemeMode {
  return darkQuery()?.matches ? "dark" : "light";
}

/** The person's explicit choice, or `null` while they are still following the OS. */
export function getModeOverride(): ThemeMode | null {
  const stored = localStorage.getItem(MODE_KEY);
  return isMode(stored) ? stored : null;
}

/** The mode actually in force: the override if there is one, else the OS preference. */
export function getMode(): ThemeMode {
  return getModeOverride() ?? getSystemMode();
}

/** Pin the mode. Survives a reload and stops following the OS until {@link followSystemMode}. */
export function setMode(mode: ThemeMode): void {
  localStorage.setItem(MODE_KEY, mode);
  notify();
}

/** Drop the override and go back to following the OS. */
export function followSystemMode(): void {
  localStorage.removeItem(MODE_KEY);
  notify();
}

/**
 * Subscribe to mode changes (for `useSyncExternalStore`). Returns an unsubscribe fn.
 *
 * Three sources, because there are three ways the answer can change: this tab calling `setMode`,
 * another tab writing the same key, and the OS flipping its preference while no override is set.
 */
export function subscribe(listener: () => void): () => void {
  listeners.add(listener);

  const onStorage = (e: StorageEvent) => {
    if (e.key === MODE_KEY) listener();
  };
  window.addEventListener("storage", onStorage);

  // Notified even when an override is set: `getMode` is what decides whether the OS still matters,
  // and `useSyncExternalStore` only re-renders when its snapshot actually changes.
  const query = darkQuery();
  query?.addEventListener?.("change", listener);

  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
    query?.removeEventListener?.("change", listener);
  };
}

/**
 * The Theme Mode in force, reactively. The *control* that calls {@link setMode} is the rail's
 * theme menu (P1T-161); this is the mechanism underneath it.
 */
export function useThemeMode(): ThemeMode {
  return useSyncExternalStore(subscribe, getMode, () => "light");
}

/**
 * What the *control* shows as chosen, which is not the same question as {@link getMode}: "System"
 * is a third state, and the difference between it and whichever mode the OS currently asks for is
 * invisible to `getMode` by design.
 *
 * A two-state toggle would pin an override on first use and never let go of it, which would make
 * {@link followSystemMode} unreachable — a control that cannot get back to the default it shipped
 * with. Hence three choices in the menu and this snapshot behind them.
 */
export type ThemeModeChoice = ThemeMode | "system";

export function getModeChoice(): ThemeModeChoice {
  return getModeOverride() ?? "system";
}

export function useThemeModeChoice(): ThemeModeChoice {
  return useSyncExternalStore(subscribe, getModeChoice, () => "system");
}
