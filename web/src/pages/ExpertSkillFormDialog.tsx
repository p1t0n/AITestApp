import { useState } from "react";
import { Autocomplete, MenuItem, TextField } from "@mui/material";
import type { SaveExpertSkill, SkillDto, SkillLevel } from "../types";
import { useSkills } from "../api";
import FormDialog, { useSaveAndClose } from "../components/FormDialog";

const LEVELS: SkillLevel[] = ["Beginner", "Intermediate", "Advanced", "Expert"];

interface Props {
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
  title,
  initial,
  lockedSkillName,
  onClose,
  onSave,
}: Props) {
  const [form, setForm] = useState<SaveExpertSkill>({ ...empty, ...initial });
  const { save, saving, error } = useSaveAndClose(onSave, onClose);
  const { data: catalogSkills, isLoading: skillsLoading } = useSkills();

  const options: SkillDto[] = catalogSkills ?? [];
  const selected = options.find((s) => s.id === form.skillId) ?? null;

  return (
    <FormDialog
      title={title}
      error={error}
      saving={saving}
      canSave={form.skillId !== ""}
      onClose={onClose}
      onSave={() => save(form)}
    >
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
    </FormDialog>
  );
}
