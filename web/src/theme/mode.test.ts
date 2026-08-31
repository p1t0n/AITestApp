// The Theme Mode mechanism. The *control* that flips it ships with the left rail (P1T-161); what
// is tested here is the store underneath it, which is the part with the interesting cases: three
// separate sources can change the answer, and one of them (the OS) only counts while nobody has
// overridden it.
import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  followSystemMode,
  getMode,
  getModeOverride,
  getSystemMode,
  setMode,
  subscribe,
  useThemeMode,
} from "./mode";

const MODE_KEY = "em.theme.mode";

/** The listeners a stubbed `matchMedia` has handed out, so a test can fire an OS change. */
let osListeners: (() => void)[] = [];

/**
 * Stubs `prefers-color-scheme`. jsdom implements no `matchMedia` at all, which is also the real
 * environment `mode.ts` guards for — so "no stub" is a case worth keeping, not a gap.
 */
function stubSystem(dark: boolean) {
  osListeners = [];
  const query = {
    matches: dark,
    addEventListener: (_: string, l: () => void) => void osListeners.push(l),
    removeEventListener: (_: string, l: () => void) => {
      osListeners = osListeners.filter((x) => x !== l);
    },
  };
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => query),
  );
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("what mode is in force", () => {
  it("follows the OS while nobody has chosen", () => {
    stubSystem(true);
    expect(getModeOverride()).toBeNull();
    expect(getSystemMode()).toBe("dark");
    expect(getMode()).toBe("dark");
  });

  it("falls back to light where the browser cannot answer", () => {
    // No `matchMedia` — jsdom, and any environment old enough to lack it. Light is the safer guess:
    // a light app on a dark OS is plain, a dark app where none was asked for is a bug report.
    expect(typeof window.matchMedia).toBe("undefined");
    expect(getSystemMode()).toBe("light");
    expect(getMode()).toBe("light");
  });

  it("lets an explicit choice beat the OS, and survive a reload", () => {
    stubSystem(true);
    setMode("light");

    expect(localStorage.getItem(MODE_KEY)).toBe("light");
    expect(getModeOverride()).toBe("light");
    expect(getMode()).toBe("light");
    // The OS has not changed its mind; it simply stopped being the answer.
    expect(getSystemMode()).toBe("dark");
  });

  it("goes back to following the OS when the choice is dropped", () => {
    stubSystem(true);
    setMode("light");
    followSystemMode();

    expect(localStorage.getItem(MODE_KEY)).toBeNull();
    expect(getMode()).toBe("dark");
  });

  it("ignores a stored value that is not a mode", () => {
    // Nothing writes this but a person with devtools — and a typo must not leave the app unstyled.
    localStorage.setItem(MODE_KEY, "midnight");
    expect(getModeOverride()).toBeNull();
    expect(getMode()).toBe("light");
  });
});

describe("who gets told", () => {
  it("notifies this tab on a choice and on dropping it", () => {
    const listener = vi.fn();
    const unsubscribe = subscribe(listener);

    setMode("dark");
    expect(listener).toHaveBeenCalledTimes(1);

    followSystemMode();
    expect(listener).toHaveBeenCalledTimes(2);

    unsubscribe();
    setMode("light");
    expect(listener).toHaveBeenCalledTimes(2);
  });

  it("notifies on another tab's write, and only for this key", () => {
    const listener = vi.fn();
    const unsubscribe = subscribe(listener);

    window.dispatchEvent(new StorageEvent("storage", { key: "em.session.token" }));
    expect(listener).not.toHaveBeenCalled();

    window.dispatchEvent(new StorageEvent("storage", { key: MODE_KEY }));
    expect(listener).toHaveBeenCalledTimes(1);

    unsubscribe();
    window.dispatchEvent(new StorageEvent("storage", { key: MODE_KEY }));
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("notifies when the OS flips, and lets go of the query on unsubscribe", () => {
    stubSystem(false);
    const listener = vi.fn();
    const unsubscribe = subscribe(listener);
    expect(osListeners).toHaveLength(1);

    osListeners[0]();
    expect(listener).toHaveBeenCalledTimes(1);

    unsubscribe();
    expect(osListeners).toHaveLength(0);
  });
});

describe("useThemeMode", () => {
  it("re-renders on a choice made anywhere", () => {
    stubSystem(true);
    const { result } = renderHook(() => useThemeMode());
    expect(result.current).toBe("dark");

    act(() => setMode("light"));
    expect(result.current).toBe("light");

    act(() => followSystemMode());
    expect(result.current).toBe("dark");
  });

  it("re-renders when the OS flips and no choice is standing in the way", () => {
    const query = { matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() };
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => query),
    );

    const { result } = renderHook(() => useThemeMode());
    expect(result.current).toBe("light");

    const notify = query.addEventListener.mock.calls[0][1] as () => void;
    act(() => {
      query.matches = true;
      notify();
    });

    expect(result.current).toBe("dark");
  });
});
