import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ConfirmDialog from "./ConfirmDialog";

/**
 * The one destructive question (EXP-81).
 *
 * Four copies of this dialog existed — two in `UsersPage`, one in `ExpertFormDialog`, one inline in
 * `ExpertOwnership` — differing only in their strings. What the copies agreed on, and what this
 * holds, is the shape: a `sm` dialog, the consequence as prose, Cancel first, and the destructive
 * button second, `color="error"` and spent while the mutation is in flight.
 */
describe("ConfirmDialog", () => {
  const props = {
    title: "Delete nobody@example.com?",
    body: "This removes the account and its passkeys.",
    confirmLabel: "Delete account",
    busy: false,
    onClose: () => {},
    onConfirm: () => {},
  };

  it("asks the question with its consequence and names the destructive action", () => {
    render(<ConfirmDialog {...props} />);

    expect(screen.getByRole("heading", { name: "Delete nobody@example.com?" })).toBeInTheDocument();
    expect(
      screen.getByText("This removes the account and its passkeys."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Delete account" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
  });

  it("confirms and cancels through its own callbacks", async () => {
    const onConfirm = vi.fn();
    const onClose = vi.fn();
    render(<ConfirmDialog {...props} onConfirm={onConfirm} onClose={onClose} />);

    await userEvent.click(screen.getByRole("button", { name: "Delete account" }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("spends the destructive button while the write is in flight", () => {
    render(<ConfirmDialog {...props} busy />);

    expect(screen.getByRole("button", { name: "Delete account" })).toBeDisabled();
  });

  // The hook is passed through rather than owned, because `users-role-confirm` is frozen DOM
  // (`frozenHooks.test.ts`) and the demotion question is the only one of the four that carries it.
  it("passes a test hook through to the dialog, and emits none without one", () => {
    const { unmount } = render(<ConfirmDialog {...props} data-testid="users-role-confirm" />);
    expect(screen.getByTestId("users-role-confirm")).toBeInTheDocument();
    unmount();

    render(<ConfirmDialog {...props} />);
    expect(screen.getByRole("dialog")).not.toHaveAttribute("data-testid");
  });
});
