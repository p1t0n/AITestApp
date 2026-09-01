import { useState } from "react";
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
} from "@mui/material";
import type { SaveAvailabilityEntry } from "../types";
import { apiErrorMessage } from "../api";

interface Props {
  open: boolean;
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
export default function AvailabilityFormDialog({ open, title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveAvailabilityEntry>({ ...empty, ...initial });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await onSave(form);
      onClose();
    } catch (err) {
      // The dialog stays open with the input intact so the message is actionable.
      setError(apiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField
            type="date"
            label="Effective from"
            InputLabelProps={{ shrink: true }}
            value={form.effectiveFrom}
            onChange={(e) => setForm((f) => ({ ...f, effectiveFrom: e.target.value }))}
            fullWidth
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
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          onClick={handleSave}
          disabled={saving || !form.effectiveFrom}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
