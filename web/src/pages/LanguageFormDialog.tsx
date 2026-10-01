import { useState } from "react";
import { MenuItem, TextField } from "@mui/material";
import type { LanguageLevel, SaveSpokenLanguage } from "../types";
import FormDialog, { useSaveAndClose } from "../components/FormDialog";

const LEVELS: LanguageLevel[] = ["Basic", "Conversational", "Professional", "Fluent", "Native"];

interface Props {
  title: string;
  initial?: Partial<SaveSpokenLanguage>;
  onClose: () => void;
  onSave: (dto: SaveSpokenLanguage) => Promise<unknown>;
}

const empty: SaveSpokenLanguage = { language: "", level: "Professional" };

export default function LanguageFormDialog({ title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveSpokenLanguage>({ ...empty, ...initial });
  const { save, saving, error } = useSaveAndClose(onSave, onClose);

  return (
    <FormDialog
      title={title}
      error={error}
      saving={saving}
      onClose={onClose}
      onSave={() => save(form)}
    >
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
    </FormDialog>
  );
}
