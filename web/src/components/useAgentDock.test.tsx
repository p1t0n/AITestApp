import { describe, expect, it } from "vitest";
import { renderHook } from "@testing-library/react";
import { DOCK_PUSH_VAR, useDockPush, type AgentDock } from "./useAgentDock";

// The dock is position:fixed, so a docked sidebar would sit on top of the app. `useDockPush`
// publishes how much of the viewport it covers as a custom property and the shell pads by it —
// which is what keeps App from having to know `docked`, `width`, or the 600px breakpoint (P1T-154).

function dockWith(over: Partial<AgentDock>): AgentDock {
  return {
    open: true,
    docked: true,
    width: 460,
    isNarrow: false,
    toggleOpen: () => {},
    close: () => {},
    setDocked: () => {},
    setWidth: () => {},
    ...over,
  };
}

function publishedPush(): string {
  return document.documentElement.style.getPropertyValue(DOCK_PUSH_VAR);
}

describe("useDockPush", () => {
  it("publishes the dock's width while it is docked on a wide viewport", () => {
    renderHook(() => useDockPush(dockWith({})));

    expect(publishedPush()).toBe("460px");
  });

  it("publishes nothing while the dock is closed", () => {
    renderHook(() => useDockPush(dockWith({ open: false })));

    expect(publishedPush()).toBe("0px");
  });

  it("publishes nothing for a floating bubble, which overlays on purpose", () => {
    renderHook(() => useDockPush(dockWith({ docked: false })));

    expect(publishedPush()).toBe("0px");
  });

  it("publishes nothing on a narrow viewport, where the dock takes the whole screen", () => {
    renderHook(() => useDockPush(dockWith({ isNarrow: true })));

    expect(publishedPush()).toBe("0px");
  });

  it("tracks a resize", () => {
    const { rerender } = renderHook((width: number) => useDockPush(dockWith({ width })), {
      initialProps: 460,
    });
    expect(publishedPush()).toBe("460px");

    rerender(600);

    expect(publishedPush()).toBe("600px");
  });

  it("drops the property when the dock unmounts, so signing out closes the gap", () => {
    const { unmount } = renderHook(() => useDockPush(dockWith({})));
    expect(publishedPush()).toBe("460px");

    unmount();

    // Removed rather than zeroed: the shell's `var(…, 0px)` fallback is what closes the gap.
    expect(document.documentElement.style.getPropertyValue(DOCK_PUSH_VAR)).toBe("");
  });
});
