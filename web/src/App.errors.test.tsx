import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import App from "./App";

// The shell is signed in as staff, and the rail's user block reads the session email — a session
// with no email is the honest default for a test that never ran a ceremony (P1T-161). The role is
// what puts the staff routes and the staff rail on screen at all (P1T-181).
vi.mock("./auth/useAuth", () => ({
  useIsAuthenticated: () => true,
  useSessionEmail: () => null,
  useSessionRole: () => "Administrator",
}));

// The catalog page throws on render; the roster page is a stand-in so recovery is observable
// without reaching the network.
vi.mock("./pages/CatalogPage", () => ({
  default: () => {
    throw new Error("catalog page exploded");
  },
}));
vi.mock("./pages/ExpertsPage", () => ({ default: () => <div>the roster page</div> }));

// React 19 also *reports* a render error it hit during a concurrent render — React Router 8 renders
// navigation in a transition — through the global `reportError`, i.e. a window `error` event, even
// though the boundary below catches it and the page recovers. That report is the planted error
// doing what it was planted to do, so it is claimed here rather than left to Vitest, which counts
// any unclaimed window error as a failed run. Only the planted error is claimed: anything else
// reported during a test still fails it, in afterEach.
let consoleError: ReturnType<typeof vi.spyOn>;
let reported: unknown[];
const claim = (e: ErrorEvent) => {
  reported.push(e.error);
  e.preventDefault();
};
const isPlanted = (error: unknown): boolean =>
  error instanceof Error &&
  (error.message === "catalog page exploded" || isPlanted((error as { cause?: unknown }).cause));

beforeEach(() => {
  consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
  reported = [];
  window.addEventListener("error", claim);
});
afterEach(() => {
  window.removeEventListener("error", claim);
  consoleError.mockRestore();
  expect(reported.filter((e) => !isPlanted(e))).toEqual([]);
});

describe("routed-area error boundary (P1T-153)", () => {
  it("renders a fallback with a way back instead of a white page", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/catalog"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("This page stopped working");
    expect(screen.getByRole("alert")).toHaveTextContent("catalog page exploded");
    // The shell around the routed area is untouched — the nav is still there to navigate with.
    expect(screen.getByText("ExpertToJob")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Users" })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Back to CVs" }));
    expect(screen.getByText("the roster page")).toBeInTheDocument();
    expect(screen.queryByText("This page stopped working")).not.toBeInTheDocument();
  });

  it("clears the fallback when the user navigates away on their own", async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/catalog"]}>
        <App />
      </MemoryRouter>,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("This page stopped working");
    await user.click(screen.getByRole("link", { name: "CVs" }));
    expect(screen.getByText("the roster page")).toBeInTheDocument();
  });
});
