import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import ExpiryBanner from "../components/ExpiryBanner";
import type { AccessView } from "../api";

let access: Partial<AccessView> | undefined;
let accessFailed = false;

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  return {
    ...actual,
    useMyAccessView: () => ({
      data: accessFailed ? undefined : access,
      isError: accessFailed,
      error: null,
    }),
  };
});

function renderBanner() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ExpiryBanner />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  accessFailed = false;
  access = { expiringSoon: false, expiresAt: null, retentionClock: "Claimed" };
});

describe("the expiry warning (P1T-188)", () => {
  it("says nothing until the record is inside its final thirty days", () => {
    const { container } = renderBanner();

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing at all for an account that owns no record", () => {
    accessFailed = true;
    const { container } = renderBanner();

    expect(container).toBeEmptyDOMElement();
  });

  /**
   * The date is the point: Art. 15(1)(d) asks for the period, and the person's own date is the form
   * of it they can act on.
   */
  it("names the date the record goes", () => {
    access = {
      expiringSoon: true,
      expiresAt: "2026-10-01T00:00:00Z",
      retentionClock: "Claimed",
    };
    renderBanner();

    expect(screen.getByRole("alert")).toHaveTextContent(/due to be deleted on/);
  });

  /**
   * The warning cures the thing it warns about — for an owned record, reading it is activity. Saying
   * so matters: a warning that quietly fixed itself would leave somebody thinking they still had to
   * act, and a warning that did not say so would be mildly dishonest about the deadline it just moved.
   */
  it("tells the owner that reading it has already moved the date", () => {
    access = { expiringSoon: true, expiresAt: "2026-10-01T00:00:00Z", retentionClock: "Claimed" };
    renderBanner();

    expect(screen.getByRole("alert")).toHaveTextContent(/Signing in and reading this counts/);
    expect(screen.getByRole("alert")).toHaveTextContent(/Nothing is required of you/);
  });

  /**
   * An unclaimed record is a different situation with a different remedy: nothing the reader does
   * passively will save it, and claiming it is what would.
   */
  it("tells an unclaimed record's reader that claiming it is what keeps it", () => {
    access = { expiringSoon: true, expiresAt: "2026-10-01T00:00:00Z", retentionClock: "Unclaimed" };
    renderBanner();

    expect(screen.getByRole("alert")).toHaveTextContent(/Claiming this record keeps it/);
    expect(screen.getByRole("alert")).not.toHaveTextContent(/Signing in and reading this counts/);
  });
});
