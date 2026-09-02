import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import EraseAccountControl, { ERASE_CONSEQUENCE } from "../components/EraseAccountControl";
import MyWorkspacePage from "./MyWorkspacePage";
import { PAUSE_CONSEQUENCE } from "../components/BenchPauseControl";

const erase = vi.fn();

vi.mock("../api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api")>();
  const idle = { isPending: false, isError: false, error: null, isSuccess: false };
  return {
    ...actual,
    useEraseMyAccount: () => ({ mutate: erase, ...idle }),
    useMyVisibility: () => ({
      data: { expertId: "e1", hidden: false, hiddenSince: null },
      isError: false,
      error: null,
    }),
    useSetMyVisibility: () => ({ mutate: vi.fn(), ...idle }),
    useRedeemClaimCode: () => ({ mutate: vi.fn(), ...idle }),
    useNoticeStatus: () => ({ data: { pendingVersion: null } }),
    useAcknowledgeNotice: () => ({ mutate: vi.fn(), ...idle }),
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

beforeEach(() => vi.clearAllMocks());

describe("deleting yourself (P1T-186)", () => {
  /**
   * The clause people skip is the one that matters: there is no email on this service, so nothing
   * about this is recoverable and nobody can be told it happened.
   */
  it("says what is deleted, what survives, and that there is no way back", () => {
    renderIn(<EraseAccountControl />);

    expect(screen.getByText(ERASE_CONSEQUENCE)).toBeInTheDocument();
    expect(ERASE_CONSEQUENCE).toMatch(/cannot be undone/);
    expect(ERASE_CONSEQUENCE).toMatch(/no email on this service/);
    expect(ERASE_CONSEQUENCE).toMatch(/keep their decision/);
  });

  it("points at the pause for somebody who meant that instead", () => {
    renderIn(<EraseAccountControl />);

    expect(screen.getByText(/If you only want to stop being offered for work/)).toBeInTheDocument();
  });

  it("will not delete without the control word, and sends it when given", async () => {
    renderIn(<EraseAccountControl />);

    await userEvent.click(screen.getByRole("button", { name: "Delete my account and my record" }));

    const dialog = screen.getByRole("dialog");
    const submit = within(dialog).getByRole("button", { name: "Delete everything" });
    expect(submit).toBeDisabled();
    expect(erase).not.toHaveBeenCalled();

    await userEvent.type(within(dialog).getByLabelText("Your control word"), "hunter2");
    await userEvent.click(submit);

    expect(erase).toHaveBeenCalledWith("hunter2", expect.anything());
  });

  it("repeats the consequence inside the confirmation, not only above the button", async () => {
    renderIn(<EraseAccountControl />);
    await userEvent.click(screen.getByRole("button", { name: "Delete my account and my record" }));

    expect(screen.getByRole("dialog")).toHaveTextContent(ERASE_CONSEQUENCE);
  });

  /**
   * P1T-171 chose two separate controls precisely so nobody deletes when they meant to pause. That
   * survives only if the page keeps them apart — so the order is asserted rather than assumed.
   */
  it("keeps pause and delete apart on the page, in that order", () => {
    renderIn(<MyWorkspacePage />);

    const pause = screen.getByText(PAUSE_CONSEQUENCE);
    const destroy = screen.getByText(ERASE_CONSEQUENCE);

    expect(pause).toBeInTheDocument();
    expect(destroy).toBeInTheDocument();
    const order = pause.compareDocumentPosition(destroy) & Node.DOCUMENT_POSITION_FOLLOWING;
    expect(order).toBeTruthy();

    expect(screen.getByRole("button", { name: /^Pause/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Delete my account and my record" })).toBeInTheDocument();
  });
});
