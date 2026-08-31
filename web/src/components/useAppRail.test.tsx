import { afterEach, describe, expect, it } from "vitest";
import { renderHook } from "@testing-library/react";
import {
  RAIL_COLLAPSED_WIDTH,
  RAIL_CONTENT_FLOOR,
  RAIL_PUSH_VAR,
  RAIL_WIDTH,
  railSqueezeQuery,
  useRailPush,
  type AppRail,
} from "./useAppRail";

// The rail is the left-hand twin of the dock: `position: fixed`, publishes what it covers, takes no
// part in layout. `useAgentDock.test.tsx` is the same suite for the other edge — the two are
// deliberately parallel, because the shell's whole claim is that it treats them identically.

function railWith(over: Partial<AppRail>): AppRail {
  return {
    collapsed: false,
    squeezed: false,
    isNarrow: false,
    drawerOpen: false,
    width: RAIL_WIDTH,
    toggleCollapsed: () => {},
    openDrawer: () => {},
    closeDrawer: () => {},
    ...over,
  };
}

function publishedPush(): string {
  return document.documentElement.style.getPropertyValue(RAIL_PUSH_VAR);
}

afterEach(() => document.documentElement.style.removeProperty(RAIL_PUSH_VAR));

describe("useRailPush", () => {
  it("publishes the rail's width while it is standing beside the app", () => {
    renderHook(() => useRailPush(railWith({})));

    expect(publishedPush()).toBe("240px");
  });

  it("publishes the collapsed width once the rail is icons only", () => {
    renderHook(() => useRailPush(railWith({ collapsed: true, width: RAIL_COLLAPSED_WIDTH })));

    expect(publishedPush()).toBe("64px");
  });

  it("publishes nothing as a drawer, which overlays the app on purpose", () => {
    renderHook(() => useRailPush(railWith({ isNarrow: true, width: 0 })));

    expect(publishedPush()).toBe("0px");
  });

  it("tracks a collapse", () => {
    const { rerender } = renderHook((width: number) => useRailPush(railWith({ width })), {
      initialProps: RAIL_WIDTH,
    });
    expect(publishedPush()).toBe("240px");

    rerender(RAIL_COLLAPSED_WIDTH);

    expect(publishedPush()).toBe("64px");
  });

  it("drops the property when the rail unmounts, so the auth pages get no gutter", () => {
    const { unmount } = renderHook(() => useRailPush(railWith({})));
    expect(publishedPush()).toBe("240px");

    unmount();

    // Removed rather than zeroed: the shell's `var(…, 0px)` fallback is what closes the gap.
    expect(publishedPush()).toBe("");
  });
});

describe("the squeeze rule", () => {
  // The one number behind "the rail collapses before the content does". These are the two viewport
  // widths P1T-161 names, so the reason the content is legible at each is written down, not lucky.
  const DOCK_DEFAULT = 420;

  /** What a browser would answer for `railSqueezeQuery`, given a viewport. */
  function matches(query: string, viewportWidth: number): boolean {
    const max = Number(/max-width:([\d.]+)px/.exec(query)![1]);
    return viewportWidth <= max;
  }

  it("forces the rail collapsed at 1280px with the dock docked open", () => {
    expect(matches(railSqueezeQuery(DOCK_DEFAULT), 1280)).toBe(true);

    // …and that is what keeps the content above its floor: 1280 - 420 - 64 = 796.
    expect(1280 - DOCK_DEFAULT - RAIL_COLLAPSED_WIDTH).toBeGreaterThanOrEqual(RAIL_CONTENT_FLOOR);
  });

  it("leaves the rail expanded at 1440px with the dock docked open", () => {
    expect(matches(railSqueezeQuery(DOCK_DEFAULT), 1440)).toBe(false);

    // An expanded rail is affordable here, which is precisely what the query says: 1440 - 420 - 240.
    expect(1440 - DOCK_DEFAULT - RAIL_WIDTH).toBeGreaterThanOrEqual(RAIL_CONTENT_FLOOR);
  });

  it("does not squeeze a wide viewport with the dock closed", () => {
    expect(matches(railSqueezeQuery(0), 1280)).toBe(false);
    // 950 - 240 = 710, under the floor: the rail yields even with no dock beside it.
    expect(matches(railSqueezeQuery(0), 950)).toBe(true);
  });

  it("moves with the dock, so resizing the dock re-decides the rail", () => {
    expect(matches(railSqueezeQuery(360), 1400)).toBe(false);
    expect(matches(railSqueezeQuery(600), 1400)).toBe(true);
  });

  it("is exactly the content floor plus an expanded rail", () => {
    expect(railSqueezeQuery(0)).toBe(`(max-width:${RAIL_CONTENT_FLOOR + RAIL_WIDTH - 0.05}px)`);
  });
});
