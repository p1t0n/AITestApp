import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { DockErrorFallback, ErrorBoundary, PageErrorFallback } from "./ErrorBoundary";

/** Throws on render while `boom` is true — the shape of a real render-time crash. */
function Boom({ boom, message = "kaboom" }: { boom: boolean; message?: string }) {
  if (boom) throw new Error(message);
  return <div>panel content</div>;
}

// React logs every caught error itself; keep the suite output readable.
let consoleError: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
});
afterEach(() => consoleError.mockRestore());

describe("ErrorBoundary (P1T-153)", () => {
  it("renders children while nothing throws", () => {
    render(
      <ErrorBoundary fallback={() => <div>fallback</div>}>
        <Boom boom={false} />
      </ErrorBoundary>,
    );
    expect(screen.getByText("panel content")).toBeInTheDocument();
    expect(screen.queryByText("fallback")).not.toBeInTheDocument();
  });

  it("catches a render throw and hands the error to the fallback", () => {
    render(
      <ErrorBoundary fallback={(error) => <div>caught: {error.message}</div>}>
        <Boom boom message="the panel exploded" />
      </ErrorBoundary>,
    );
    expect(screen.getByText("caught: the panel exploded")).toBeInTheDocument();
  });

  it("retries in place when the fallback calls reset", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [boom, setBoom] = useState(true);
      return (
        <ErrorBoundary
          fallback={(_, reset) => (
            <button
              onClick={() => {
                setBoom(false);
                reset();
              }}
            >
              retry
            </button>
          )}
        >
          <Boom boom={boom} />
        </ErrorBoundary>
      );
    }

    render(<Harness />);
    await user.click(screen.getByRole("button", { name: "retry" }));
    expect(screen.getByText("panel content")).toBeInTheDocument();
  });

  it("clears the error on its own when resetKey changes", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [key, setKey] = useState("a");
      return (
        <>
          <button onClick={() => setKey("b")}>switch</button>
          <ErrorBoundary resetKey={key} fallback={() => <div>fallback</div>}>
            <Boom boom={key === "a"} />
          </ErrorBoundary>
        </>
      );
    }

    render(<Harness />);
    expect(screen.getByText("fallback")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "switch" }));
    expect(screen.getByText("panel content")).toBeInTheDocument();
  });

  it("keeps siblings outside the boundary rendering", () => {
    render(
      <>
        <div>the roster</div>
        <ErrorBoundary fallback={() => <div>fallback</div>}>
          <Boom boom />
        </ErrorBoundary>
      </>,
    );
    expect(screen.getByText("the roster")).toBeInTheDocument();
    expect(screen.getByText("fallback")).toBeInTheDocument();
  });
});

describe("PageErrorFallback (P1T-153)", () => {
  it("shows the failure and a route back to the roster", async () => {
    const user = userEvent.setup();
    const reset = vi.fn();
    render(
      <MemoryRouter initialEntries={["/catalog"]}>
        <PageErrorFallback error={new Error("render blew up")} reset={reset} />
      </MemoryRouter>,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("This page stopped working");
    expect(screen.getByRole("alert")).toHaveTextContent("render blew up");
    await user.click(screen.getByRole("button", { name: "Back to CVs" }));
    // Reset and navigate together, so the button also works when the crashed route *is* "/".
    expect(reset).toHaveBeenCalledOnce();
  });
});

describe("DockErrorFallback (P1T-153)", () => {
  it("stays inside the dock and offers a retry", async () => {
    const user = userEvent.setup();
    const reset = vi.fn();
    render(<DockErrorFallback error={new Error("agent panel blew up")} reset={reset} />);

    expect(screen.getByRole("alert")).toHaveTextContent("This panel stopped working");
    expect(screen.getByRole("alert")).toHaveTextContent("agent panel blew up");
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(reset).toHaveBeenCalledOnce();
  });
});
