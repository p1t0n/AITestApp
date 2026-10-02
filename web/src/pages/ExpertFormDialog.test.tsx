// The expert form's two failure paths, held while the save half moves onto `useSaveAndClose`
// (EXP-104). Saving and deleting fail differently and the dialog has to say so: a failed save keeps
// the form open with the typing in it, because a server validation message is only actionable next
// to the field that caused it; a failed delete drops the confirmation and reports it on the form
// behind it, so the record is visibly still there.
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, AxiosHeaders } from "axios";
import ExpertFormDialog from "./ExpertFormDialog";

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

const INITIAL = { firstName: "Ada", lastName: "Lovelace", title: "Engineer", email: "ada@x.io" };

function renderForm(props: {
  onSave?: () => Promise<unknown>;
  onClose?: () => void;
  onDelete?: () => Promise<unknown>;
}) {
  const onSave = props.onSave ?? vi.fn().mockResolvedValue({});
  const onClose = props.onClose ?? vi.fn();
  render(
    <ExpertFormDialog
      open
      title="Edit expert"
      initial={INITIAL}
      onClose={onClose}
      onSave={onSave}
      onDelete={props.onDelete}
      deleteSubject={props.onDelete ? "Ada Lovelace" : undefined}
    />,
  );
  return { onSave, onClose };
}

/** Save is spent until the form is dirty, so type something first. */
async function dirty() {
  await userEvent.type(screen.getByLabelText("Title"), " II");
}

describe("saving the expert form", () => {
  it("sends the edited record and closes", async () => {
    const { onSave, onClose } = renderForm({});
    await dirty();

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ title: "Engineer II" }));
    expect(onClose).toHaveBeenCalled();
  });

  it("keeps the dialog open with the typing in it when the server refuses", async () => {
    const onSave = vi.fn().mockRejectedValue(validationFailure("Title is required."));
    const { onClose } = renderForm({ onSave });
    await dirty();

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Title is required.")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Title")).toHaveValue("Engineer II");
  });
});

describe("deleting from inside the expert form", () => {
  it("closes the form once the record is gone", async () => {
    const onDelete = vi.fn().mockResolvedValue({});
    const { onClose } = renderForm({ onDelete });

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    await userEvent.click(screen.getByRole("button", { name: "Delete expert" }));

    expect(onDelete).toHaveBeenCalled();
    expect(onClose).toHaveBeenCalled();
  });

  it("reports its own failure on the form, with the confirmation dropped", async () => {
    const onDelete = vi.fn().mockRejectedValue(validationFailure("Expert has an active claim."));
    const { onClose } = renderForm({ onDelete });

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    await userEvent.click(screen.getByRole("button", { name: "Delete expert" }));

    expect(await screen.findByText("Expert has an active claim.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Delete expert" })).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });
});
