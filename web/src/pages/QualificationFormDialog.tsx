import { useState } from "react";
import { MenuItem, Stack, TextField } from "@mui/material";
import type { QualificationType, SaveQualification } from "../types";
import FormDialog, { useSaveAndClose } from "../components/FormDialog";

const TYPES: QualificationType[] = ["Degree", "Certification"];

interface Props {
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
export default function QualificationFormDialog({ title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveQualification>({ ...empty, ...initial });
  const { save, saving, error } = useSaveAndClose(onSave, onClose);

  const text =
    (key: keyof SaveQualification) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [key]: e.target.value === "" ? null : e.target.value }));

  const isDegree = form.type === "Degree";

  // Only the fields belonging to the selected type are sent; the others are cleared so a record
  // switched from Degree to Certification does not keep an institution behind it.
  const payload = () =>
    isDegree
      ? { ...form, issuer: null, credentialId: null, issueDate: null, expiryDate: null }
      : { ...form, institution: null, field: null, startDate: null, endDate: null };

  const date = (label: string, key: keyof SaveQualification) => (
    <TextField
      type="date"
      label={label}
      value={(form[key] as string | null) ?? ""}
      onChange={text(key)}
      fullWidth
      slotProps={{
        inputLabel: { shrink: true }
      }}
    />
  );

  return (
    <FormDialog
      title={title}
      error={error}
      saving={saving}
      maxWidth="sm"
      onClose={onClose}
      onSave={() => save(payload())}
    >
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
    </FormDialog>
  );
}
