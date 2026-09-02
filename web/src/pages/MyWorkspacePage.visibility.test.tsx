import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import BenchPauseControl, { PAUSE_CONSEQUENCE } from "../components/BenchPauseControl";

type Visibility = { expertId: string; hidden: boolean; hiddenSince: string | null };

let visibility: Visibility | undefined = { expertId: "e1", hidden: false, hiddenSince: null };
let visibilityError = false;
const setVisibility = vi.fn();

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useMyVisibility: () => ({
      data: visibilityError ? undefined : visibility,
      isError: visibilityError,
      error: null,
    }),
    useSetMyVisibility: () => ({
      mutate: setVisibility,
      isPending: false,
      isError: false,
      error: null,
    }),
  };
});

function renderIn(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{node}</MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  visibility = { expertId: "e1", hidden: false, hiddenSince: null };
  visibilityError = false;
  vi.clearAllMocks();
});

describe("the Expert's pause control (P1T-185)", () => {
  /**
   * People choose between this and deletion, and the service can never mail anybody a way back —
   * so what a pause does, and what it does not do, has to be in front of them before they press it.
   */
  it("says what pausing does and what it leaves alone", () => {
    renderIn(<BenchPauseControl />);

    expect(screen.getByText(PAUSE_CONSEQUENCE)).toBeInTheDocument();
    expect(PAUSE_CONSEQUENCE).toMatch(/Nothing is deleted/);
    expect(PAUSE_CONSEQUENCE).toMatch(/come back whenever/);
  });

  it("pauses and resumes through the one control", async () => {
    renderIn(<BenchPauseControl />);

    await userEvent.click(screen.getByRole("button", { name: /Pause/ }));
    expect(setVisibility).toHaveBeenCalledWith(true);

    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T10:00:00Z" };
    renderIn(<BenchPauseControl />);

    await userEvent.click(screen.getByRole("button", { name: /Start being offered for work again/ }));
    expect(setVisibility).toHaveBeenCalledWith(false);
  });

  it("says since when, because the transparency view has to", () => {
    visibility = { expertId: "e1", hidden: true, hiddenSince: "2026-09-01T10:00:00Z" };
    renderIn(<BenchPauseControl />);

    expect(screen.getByText(/You paused yourself on/)).toBeInTheDocument();
  });

  /** Somebody whose claim is still waiting owns no record — there is nothing to pause, and a
   * control offering to pause nothing would be a claim that they are on the bench. */
  it("renders nothing for an account that owns no record", () => {
    visibilityError = true;
    const { container } = renderIn(<BenchPauseControl />);

    expect(container).toBeEmptyDOMElement();
  });
});
