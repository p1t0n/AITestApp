import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import FormDialog, { useSaveAndClose } from "./FormDialog";

/** What the API answers on a FluentValidation failure, in the shape `apiErrorMessage` reads. */
function validationFailure(message: string) {
  return new AxiosError("Request failed", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 400,
    statusText: "Bad Request",
    data: { error: message },
    headers: new AxiosHeaders(),
    config: { headers: new AxiosHeaders() },
  });
}

/**
 * The shell the five expert-child forms share (EXP-81), and the save it does for them.
 *
 * `EditDialog` is the dictionary popup and guards *unsaved work* — a dirty chip, a discard
 * question, Save spent until something is typed. These forms are mounted only while editing and
 * ask none of that, so they get their own shell: title, error, fields, Cancel, Save. What the five
 * copies really shared was `handleSave`, which is `useSaveAndClose`.
 */

/** A form stripped to what the hook and the shell actually do. */
function Harness({
  onSave,
  onClose,
  canSave,
}: {
  onSave: (dto: { name: string }) => Promise<unknown>;
  onClose: () => void;
  canSave?: boolean;
}) {
  const { save, saving, error } = useSaveAndClose(onSave, onClose);
  return (
    <FormDialog
      title="Add thing"
      error={error}
      saving={saving}
      canSave={canSave}
      onClose={onClose}
      onSave={() => save({ name: "typed" })}
    >
      <p>the fields</p>
    </FormDialog>
  );
}

describe("FormDialog", () => {
  it("renders the title, the fields and the two actions", () => {
    render(<Harness onSave={vi.fn()} onClose={() => {}} />);

    expect(screen.getByRole("heading", { name: "Add thing" })).toBeInTheDocument();
    expect(screen.getByText("the fields")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });

  it("spends Save while a form cannot be sent yet", () => {
    render(<Harness onSave={vi.fn()} onClose={() => {}} canSave={false} />);

    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("closes on Cancel without saving", async () => {
    const onSave = vi.fn();
    const onClose = vi.fn();
    render(<Harness onSave={onSave} onClose={onClose} />);

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onSave).not.toHaveBeenCalled();
  });
});

describe("useSaveAndClose", () => {
  it("saves, then closes", async () => {
    const onSave = vi.fn().mockResolvedValue({});
    const onClose = vi.fn();
    render(<Harness onSave={onSave} onClose={onClose} />);

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(onSave).toHaveBeenCalledWith({ name: "typed" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("keeps the dialog open on a server failure and renders the message", async () => {
    const onSave = vi.fn().mockRejectedValue(validationFailure("Name must not be empty."));
    const onClose = vi.fn();
    render(<Harness onSave={onSave} onClose={onClose} />);

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Name must not be empty.");
    expect(onClose).not.toHaveBeenCalled();
    // Spent only while in flight: the message is actionable, so the retry has to be clickable.
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });

  it("clears a previous failure when the next attempt starts", async () => {
    const onSave = vi
      .fn()
      .mockRejectedValueOnce(validationFailure("Name must not be empty."))
      .mockResolvedValue({});
    const onClose = vi.fn();
    render(<Harness onSave={onSave} onClose={onClose} />);

    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
