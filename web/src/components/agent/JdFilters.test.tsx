// The one place the four agent tabs agree on what a "set" filter is. `toRequest()` is the whole
// contract: an unset filter is absent from the body, not sent as "" or 0, because the server owns
// every default. These assert the emitted object key by key, so a widened field shows up here.
import { describe, expect, it } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useJdFilters } from "./JdFilters";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

function setup() {
  return renderHook(() => useJdFilters(), { wrapper });
}

describe("useJdFilters().toRequest()", () => {
  it("sends nothing when no filter is set", () => {
    const { result } = setup();
    expect(result.current.toRequest()).toEqual({});
  });

  it("sends availableOn when a date is picked", () => {
    const { result } = setup();
    act(() => result.current.setAvailableOn("2026-08-01"));
    expect(result.current.toRequest()).toEqual({ availableOn: "2026-08-01" });
  });

  it("sends skillIds only when at least one skill is selected", () => {
    const { result } = setup();
    act(() => result.current.setSkillIds([]));
    expect(result.current.toRequest()).toEqual({});
    act(() => result.current.setSkillIds(["s1", "s2"]));
    expect(result.current.toRequest()).toEqual({ skillIds: ["s1", "s2"] });
  });

  it("trims location and drops a whitespace-only one", () => {
    const { result } = setup();
    act(() => result.current.setLocation("   "));
    expect(result.current.toRequest()).toEqual({});
    act(() => result.current.setLocation("  Berlin  "));
    expect(result.current.toRequest()).toEqual({ location: "Berlin" });
  });

  it("sends minYears as a number, including a deliberate 0", () => {
    const { result } = setup();
    act(() => result.current.setMinYears("0"));
    expect(result.current.toRequest()).toEqual({ minYears: 0 });
    act(() => result.current.setMinYears("5"));
    expect(result.current.toRequest()).toEqual({ minYears: 5 });
    act(() => result.current.setMinYears(""));
    expect(result.current.toRequest()).toEqual({});
  });

  it("sends every filter together", () => {
    const { result } = setup();
    act(() => {
      result.current.setAvailableOn("2026-08-01");
      result.current.setSkillIds(["s1"]);
      result.current.setLocation("Berlin");
      result.current.setMinYears("5");
    });
    expect(result.current.toRequest()).toEqual({
      availableOn: "2026-08-01",
      skillIds: ["s1"],
      location: "Berlin",
      minYears: 5,
    });
  });

  it("starts collapsed", () => {
    const { result } = setup();
    expect(result.current.showFilters).toBe(false);
  });
});
