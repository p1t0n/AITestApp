import { useState } from "react";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
} from "@mui/material";
import type { SaveExpert } from "../types";
import { apiErrorMessage } from "../api";
import { ErrorNotice } from "../components/ErrorNotice";

interface Props {
  open: boolean;
  title: string;
  initial?: Partial<SaveExpert>;
  onClose: () => void;
  onSave: (dto: SaveExpert) => Promise<unknown>;
}

const empty: SaveExpert = {
  firstName: "",
  lastName: "",
  title: "",
  email: "",
  phone: null,
  location: null,
  summary: null,
  photoUrl: null,
};

export default function ExpertFormDialog({ open, title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveExpert>({ ...empty, ...initial });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const field =
    (key: keyof SaveExpert) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [key]: e.target.value }));

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await onSave(form);
      onClose();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <ErrorNotice message={error} />
          <Stack direction="row" spacing={2}>
            <TextField label="First name" value={form.firstName} onChange={field("firstName")} fullWidth />
            <TextField label="Last name" value={form.lastName} onChange={field("lastName")} fullWidth />
          </Stack>
          <TextField label="Title" value={form.title} onChange={field("title")} fullWidth />
          <TextField label="Email" value={form.email} onChange={field("email")} fullWidth />
          <Stack direction="row" spacing={2}>
            <TextField label="Phone" value={form.phone ?? ""} onChange={field("phone")} fullWidth />
            <TextField label="Location" value={form.location ?? ""} onChange={field("location")} fullWidth />
          </Stack>
          <TextField
            label="Summary"
            value={form.summary ?? ""}
            onChange={field("summary")}
            fullWidth
            multiline
            minRows={3}
          />
          <TextField label="Photo URL" value={form.photoUrl ?? ""} onChange={field("photoUrl")} fullWidth />
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
