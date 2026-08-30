import { useState } from "react";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
} from "@mui/material";
import type { LanguageLevel, SaveSpokenLanguage } from "../types";
import { apiErrorMessage } from "../api";
import { ErrorNotice } from "../components/ErrorNotice";

const LEVELS: LanguageLevel[] = ["Basic", "Conversational", "Professional", "Fluent", "Native"];

interface Props {
  open: boolean;
  title: string;
  initial?: Partial<SaveSpokenLanguage>;
  onClose: () => void;
  onSave: (dto: SaveSpokenLanguage) => Promise<unknown>;
}

const empty: SaveSpokenLanguage = { language: "", level: "Professional" };

export default function LanguageFormDialog({ open, title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveSpokenLanguage>({ ...empty, ...initial });
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
          <ErrorNotice message={error} />
          <TextField
            label="Language"
            value={form.language}
            onChange={(e) => setForm((f) => ({ ...f, language: e.target.value }))}
            fullWidth
          />
          <TextField
            select
            label="Level"
            value={form.level}
            onChange={(e) => setForm((f) => ({ ...f, level: e.target.value as LanguageLevel }))}
            fullWidth
          >
            {LEVELS.map((l) => (
              <MenuItem key={l} value={l}>
                {l}
              </MenuItem>
            ))}
          </TextField>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={handleSave} disabled={saving}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
