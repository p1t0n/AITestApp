import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import EditDialog, { DISCARD_QUESTION, UNSAVED_CHANGES } from "./EditDialog";

/**
 * The popup-only editing rule's other half (EXP-46).
 *
 * `DictionaryTable` keeps writes out of the row; this keeps them from being thrown away by the same
 * stray click. A clean dialog is cheap to dismiss and dismisses; a dirty one asks. Esc and the
 * backdrop are asserted separately because they are separate MUI close reasons, and a handler that
 * only guards one of them looks right in review.
 */

function renderDialog(over: { dirty?: boolean; onClose?: () => void; onSave?: () => void } = {}) {
  const onClose = over.onClose ?? vi.fn();
  const onSave = over.onSave ?? vi.fn();
  render(
    <EditDialog
      title="Edit pet"
      dirty={over.dirty ?? false}
      onClose={onClose}
      onSave={onSave}
    >
      <input aria-label="Name" />
    </EditDialog>,
  );
  return { onClose, onSave };
}

const backdrop = () => document.querySelector(".MuiBackdrop-root") as HTMLElement;

describe("a dialog with nothing to lose", () => {
  it("closes on Esc", async () => {
    const { onClose } = renderDialog();

    await userEvent.keyboard("{Escape}");

    expect(onClose).toHaveBeenCalled();
  });

  it("closes on a backdrop click", async () => {
    const { onClose } = renderDialog();

    await userEvent.click(backdrop());

    expect(onClose).toHaveBeenCalled();
  });

  it("closes on Cancel", async () => {
    const { onClose } = renderDialog();

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onClose).toHaveBeenCalled();
  });

  it("does not claim there are unsaved changes", () => {
    renderDialog();

    expect(screen.queryByText(UNSAVED_CHANGES)).not.toBeInTheDocument();
  });

  it("has nothing to save", () => {
    renderDialog();

    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });
});

describe("a dirty dialog", () => {
  it("says so in its title", () => {
    renderDialog({ dirty: true });

    expect(screen.getByText(UNSAVED_CHANGES)).toBeVisible();
  });

  it("refuses Esc, and asks instead", async () => {
    const { onClose } = renderDialog({ dirty: true });

    await userEvent.keyboard("{Escape}");

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeVisible();
    expect(screen.getByText(DISCARD_QUESTION)).toBeVisible();
  });

  it("refuses a backdrop click, and asks instead", async () => {
    const { onClose } = renderDialog({ dirty: true });

    await userEvent.click(backdrop());

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeVisible();
    expect(screen.getByText(DISCARD_QUESTION)).toBeVisible();
  });

  it("asks on Cancel too", async () => {
    const { onClose } = renderDialog({ dirty: true });

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText(DISCARD_QUESTION)).toBeVisible();
  });

  it("takes the question back on Keep editing, and keeps the work", async () => {
    const { onClose } = renderDialog({ dirty: true });

    await userEvent.keyboard("{Escape}");
    await userEvent.click(screen.getByRole("button", { name: "Keep editing" }));

    expect(screen.queryByText(DISCARD_QUESTION)).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeVisible();
  });

  it("closes once Discard answers it", async () => {
    const { onClose } = renderDialog({ dirty: true });

    await userEvent.keyboard("{Escape}");
    await userEvent.click(screen.getByRole("button", { name: "Discard" }));

    expect(onClose).toHaveBeenCalled();
  });

  it("asks again the next time, rather than remembering the answer", async () => {
    renderDialog({ dirty: true });

    await userEvent.keyboard("{Escape}");
    await userEvent.click(screen.getByRole("button", { name: "Keep editing" }));
    // Cancel rather than Esc for the second attempt: clicking "Keep editing" unmounts the button
    // that had focus, and jsdom drops focus to the body, where MUI's own key handler never sees the
    // press. The claim under test is that the answer was not remembered, and Cancel proves it
    // through the same `tryClose`.
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.getByText(DISCARD_QUESTION)).toBeVisible();
  });

  it("saves without asking anything", async () => {
    const { onSave, onClose } = renderDialog({ dirty: true });

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(onSave).toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByText(DISCARD_QUESTION)).not.toBeInTheDocument();
  });
});

describe("while a save is in flight", () => {
  it("does not offer the button a second time", () => {
    render(
      <EditDialog title="Edit pet" dirty saving onClose={vi.fn()} onSave={vi.fn()}>
        <input aria-label="Name" />
      </EditDialog>,
    );

    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });
});
