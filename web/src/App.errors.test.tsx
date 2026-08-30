import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import App from "./App";

vi.mock("./auth/useAuth", () => ({ useIsAuthenticated: () => true }));

// The catalog page throws on render; the roster page is a stand-in so recovery is observable
// without reaching the network.
vi.mock("./pages/CatalogPage", () => ({
  default: () => {
    throw new Error("catalog page exploded");
  },
}));
vi.mock("./pages/EmployeesPage", () => ({ default: () => <div>the roster page</div> }));

let consoleError: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
});
afterEach(() => consoleError.mockRestore());

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
    expect(screen.getByText("CV Manager")).toBeInTheDocument();
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
