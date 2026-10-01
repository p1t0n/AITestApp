import { useState } from "react";
import { TextField } from "@mui/material";
import type { SaveAvailabilityEntry } from "../types";
import FormDialog, { useSaveAndClose } from "../components/FormDialog";

interface Props {
  title: string;
  initial?: Partial<SaveAvailabilityEntry>;
  onClose: () => void;
  onSave: (dto: SaveAvailabilityEntry) => Promise<unknown>;
}

const empty: SaveAvailabilityEntry = { effectiveFrom: "", capacityPercent: 100 };

/**
 * One step of the availability step function: from this date on, the expert is at this capacity.
 * Add and edit share the form, so the payload the API sees is built in one place.
 *
 * Save stays disabled until a date is typed — not client-side validation (the server is the only
 * validator, a product invariant), but because an empty date never reaches FluentValidation at all:
 * it fails `DateOnly` model binding, and a binding failure answers in a shape `apiErrorMessage`
 * cannot read back into a sentence. The inline row this replaced guarded the same way.
 */
export default function AvailabilityFormDialog({ title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveAvailabilityEntry>({ ...empty, ...initial });
  const { save, saving, error } = useSaveAndClose(onSave, onClose);

  return (
    <FormDialog
      title={title}
      error={error}
      saving={saving}
      canSave={form.effectiveFrom !== ""}
      onClose={onClose}
      onSave={() => save(form)}
    >
      <TextField
        type="date"
        label="Effective from"
        value={form.effectiveFrom}
        onChange={(e) => setForm((f) => ({ ...f, effectiveFrom: e.target.value }))}
        fullWidth
        slotProps={{
          inputLabel: { shrink: true }
        }}
      />
      <TextField
        type="number"
        label="Capacity %"
        value={form.capacityPercent}
        onChange={(e) =>
          setForm((f) => ({ ...f, capacityPercent: Number(e.target.value) }))
        }
        fullWidth
      />
    </FormDialog>
  );
}
