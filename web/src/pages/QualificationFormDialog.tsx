import { useState } from "react";
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
} from "@mui/material";
import type { QualificationType, SaveQualification } from "../types";
import { apiErrorMessage } from "../api";

const TYPES: QualificationType[] = ["Degree", "Certification"];

interface Props {
  open: boolean;
  title: string;
  initial?: Partial<SaveQualification>;
  onClose: () => void;
  onSave: (dto: SaveQualification) => Promise<unknown>;
}

const empty: SaveQualification = {
  type: "Degree",
  name: "",
  institution: null,
  field: null,
  startDate: null,
  endDate: null,
  issuer: null,
  credentialId: null,
  issueDate: null,
  expiryDate: null,
};

/**
 * One record covers both shapes the domain calls a qualification: a Degree (institution, field,
 * study dates) and a Certification (issuer, credential id, issue/expiry). Showing all ten fields at
 * once would ask a user to ignore half of them, so the type select chooses which half is rendered —
 * the unused half stays null in the payload rather than carrying stale text from the other shape.
 */
export default function QualificationFormDialog({ open, title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveQualification>({ ...empty, ...initial });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const text =
    (key: keyof SaveQualification) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [key]: e.target.value === "" ? null : e.target.value }));

  const isDegree = form.type === "Degree";

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      // Only the fields belonging to the selected type are sent; the others are cleared so a
      // record switched from Degree to Certification does not keep an institution behind it.
      await onSave(
        isDegree
          ? { ...form, issuer: null, credentialId: null, issueDate: null, expiryDate: null }
          : { ...form, institution: null, field: null, startDate: null, endDate: null },
      );
      onClose();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  const date = (label: string, key: keyof SaveQualification) => (
    <TextField
      type="date"
      label={label}
      InputLabelProps={{ shrink: true }}
      value={(form[key] as string | null) ?? ""}
      onChange={text(key)}
      fullWidth
    />
  );

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField
            select
            label="Type"
            value={form.type}
            onChange={(e) => setForm((f) => ({ ...f, type: e.target.value as QualificationType }))}
            fullWidth
          >
            {TYPES.map((t) => (
              <MenuItem key={t} value={t}>
                {t}
              </MenuItem>
            ))}
          </TextField>
          <TextField label="Name" value={form.name} onChange={text("name")} fullWidth />

          {isDegree ? (
            <>
              <TextField
                label="Institution"
                value={form.institution ?? ""}
                onChange={text("institution")}
                fullWidth
              />
              <TextField label="Field" value={form.field ?? ""} onChange={text("field")} fullWidth />
              <Stack direction="row" spacing={2}>
                {date("Start date", "startDate")}
                {date("End date", "endDate")}
              </Stack>
            </>
          ) : (
            <>
              <TextField
                label="Issuer"
                value={form.issuer ?? ""}
                onChange={text("issuer")}
                fullWidth
              />
              <TextField
                label="Credential ID"
                value={form.credentialId ?? ""}
                onChange={text("credentialId")}
                fullWidth
              />
              <Stack direction="row" spacing={2}>
                {date("Issue date", "issueDate")}
                {date("Expiry date", "expiryDate")}
              </Stack>
            </>
          )}
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
