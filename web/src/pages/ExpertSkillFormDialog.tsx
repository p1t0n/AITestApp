import { useState } from "react";
import {
  Alert,
  Autocomplete,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
} from "@mui/material";
import type { SaveExpertSkill, SkillDto, SkillLevel } from "../types";
import { apiErrorMessage, useSkills } from "../api";

const LEVELS: SkillLevel[] = ["Beginner", "Intermediate", "Advanced", "Expert"];

interface Props {
  open: boolean;
  title: string;
  initial?: Partial<SaveExpertSkill>;
  /**
   * The catalog skill this row already points at. Present only on edit, and its presence is what
   * locks the picker: the API updates the level and the years and never the link.
   */
  lockedSkillName?: string;
  onClose: () => void;
  onSave: (dto: SaveExpertSkill) => Promise<unknown>;
}

const empty: SaveExpertSkill = { skillId: "", level: "Intermediate", yearsExperience: 1 };

/**
 * A skill on an expert is a link to a catalog row plus a level and a year count. The catalog row
 * is picked, never typed — free text would invent skills outside the catalog the RAG projection is
 * built on, the same rule the experience form follows.
 *
 * On edit the picker is disabled rather than absent, because the row is *about* that skill and
 * hiding it would make the dialog ambiguous. Disabled is the honest control: the server assigns
 * `Level` and `YearsExperience` only, so an editable picker would look like it worked and change
 * nothing. Pointing the row at another skill is a delete and an add, which is what the helper says.
 */
export default function ExpertSkillFormDialog({
  open,
  title,
  initial,
  lockedSkillName,
  onClose,
  onSave,
}: Props) {
  const [form, setForm] = useState<SaveExpertSkill>({ ...empty, ...initial });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { data: catalogSkills, isLoading: skillsLoading } = useSkills();

  const options: SkillDto[] = catalogSkills ?? [];
  const selected = options.find((s) => s.id === form.skillId) ?? null;

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
          {lockedSkillName ? (
            <TextField
              label="Skill"
              value={lockedSkillName}
              disabled
              fullWidth
              helperText="Remove the skill and add another one to point this at a different catalog entry."
            />
          ) : (
            <Autocomplete
              options={options}
              value={selected}
              getOptionLabel={(o) => o.name}
              isOptionEqualToValue={(o, v) => o.id === v.id}
              loading={skillsLoading}
              onChange={(_, v) => setForm((f) => ({ ...f, skillId: v?.id ?? "" }))}
              renderInput={(params) => (
                <TextField {...params} label="Skill" placeholder="Pick from the catalog" />
              )}
            />
          )}
          <TextField
            select
            label="Level"
            value={form.level}
            onChange={(e) => setForm((f) => ({ ...f, level: e.target.value as SkillLevel }))}
            fullWidth
          >
            {LEVELS.map((l) => (
              <MenuItem key={l} value={l}>
                {l}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            type="number"
            label="Years"
            value={form.yearsExperience}
            onChange={(e) =>
              setForm((f) => ({ ...f, yearsExperience: Number(e.target.value) }))
            }
            fullWidth
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={handleSave} disabled={saving || !form.skillId}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
