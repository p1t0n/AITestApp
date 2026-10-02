// The one place the four agent tabs agree on what a "set" filter is. `toRequest()` is the whole
// contract: an unset filter is absent from the body, not sent as "" or 0, because the server owns
// every default. These assert the emitted object key by key, so a widened field shows up here.
import { describe, expect, it, vi } from "vitest";
import { act, render, renderHook, screen, waitForElementToBeRemoved } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { SkillDto } from "../../types";
import { JdFilters, useJdFilters } from "./JdFilters";

const CATALOG: SkillDto[] = [
  { id: "skill-react", name: "React", categoryId: "c1", categoryName: "Frontend", rank: 1 },
  { id: "skill-dotnet", name: ".NET", categoryId: "c2", categoryName: "Backend", rank: 2 },
];

vi.mock("../../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api")>();
  return { ...actual, useSkills: () => ({ data: CATALOG, isLoading: false }) };
});

function setup() {
  return renderHook(() => useJdFilters());
}

/** The three tabs' shape: hold the hook, hand it to the component. */
function Harness() {
  return <JdFilters filters={useJdFilters()} />;
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

  // The hook is the four filter values and the rule for turning them into a request, and nothing
  // else (EXP-104). The open/closed toggle and the skill catalog are the *component's* business —
  // three tabs hold this hook and read only `toRequest()`, so anything else here is state they
  // carry without reading.
  it("returns the four filter values and toRequest(), and nothing more", () => {
    const { result } = setup();
    expect(Object.keys(result.current).sort()).toEqual([
      "availableOn",
      "location",
      "minYears",
      "setAvailableOn",
      "setLocation",
      "setMinYears",
      "setSkillIds",
      "skillIds",
      "toRequest",
    ]);
  });
});

// The panel owns what only the panel reads (EXP-104): whether it is open, and the skill catalog it
// offers. Three tabs render it and none of them ask either question.
describe("<JdFilters />", () => {
  it("starts collapsed and opens on its own button, with no help from the tab", async () => {
    render(<Harness />);
    expect(screen.queryByLabelText("Available on")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Filters \(optional\)/ }));
    expect(screen.getByLabelText("Available on")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Filters \(optional\)/ }));
    await waitForElementToBeRemoved(() => screen.queryByLabelText("Available on"));
  });

  it("offers the catalog skills under their own names", async () => {
    render(<Harness />);
    await userEvent.click(screen.getByRole("button", { name: /Filters \(optional\)/ }));

    await userEvent.click(screen.getByLabelText("Skills"));
    expect(screen.getByRole("option", { name: "React" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: ".NET" })).toBeInTheDocument();
  });
});
