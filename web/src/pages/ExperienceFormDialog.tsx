import { useState } from "react";
import {
  Autocomplete,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  IconButton,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import ArrowDownwardIcon from "@mui/icons-material/ArrowDownward";
import ArrowUpwardIcon from "@mui/icons-material/ArrowUpward";
import DeleteIcon from "@mui/icons-material/Delete";
import type { SaveExperience, SkillDto } from "../types";
import { apiErrorMessage, useSkills } from "../api";
import { ErrorNotice } from "../components/ErrorNotice";
import { SPECIAL_CATEGORY_GUIDANCE } from "./cvGuidance";

interface Props {
  open: boolean;
  title: string;
  initial?: Partial<SaveExperience>;
  onClose: () => void;
  onSave: (dto: SaveExperience) => Promise<unknown>;
}

const empty: SaveExperience = {
  company: "",
  title: "",
  location: null,
  startDate: "",
  endDate: null,
  summary: null,
  achievements: [],
  skillIds: [],
};

/**
 * The experience write DTO carries its children: the server replaces the whole achievement list and
 * the whole skill-id list on every save. So this is one nested-collection editor rather than three
 * forms — a bullet removed here is a bullet gone after Save, and nothing is written until then.
 *
 * Bullet order is derived from position in the list at save time, so moving a bullet is enough;
 * the user never types an order number.
 */
export default function ExperienceFormDialog({ open, title, initial, onClose, onSave }: Props) {
  const [form, setForm] = useState<SaveExperience>({ ...empty, ...initial });
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const { data: catalogSkills, isLoading: skillsLoading } = useSkills();

  const options: SkillDto[] = catalogSkills ?? [];
  const selected = options.filter((s) => form.skillIds.includes(s.id));

  const text =
    (key: "company" | "title" | "location" | "startDate" | "endDate" | "summary") =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({
        ...f,
        [key]:
          e.target.value === "" && key !== "company" && key !== "title" && key !== "startDate"
            ? null
            : e.target.value,
      }));

  function setBulletText(index: number, value: string) {
    setForm((f) => ({
      ...f,
      achievements: f.achievements.map((a, i) => (i === index ? { ...a, text: value } : a)),
    }));
  }

  function addBullet() {
    setForm((f) => ({
      ...f,
      achievements: [...f.achievements, { order: f.achievements.length + 1, text: "" }],
    }));
  }

  function removeBullet(index: number) {
    setForm((f) => ({ ...f, achievements: f.achievements.filter((_, i) => i !== index) }));
  }

  function moveBullet(index: number, delta: number) {
    const target = index + delta;
    setForm((f) => {
      if (target < 0 || target >= f.achievements.length) return f;
      const next = [...f.achievements];
      [next[index], next[target]] = [next[target], next[index]];
      return { ...f, achievements: next };
    });
  }

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await onSave({
        ...form,
        // Position is the order. Blank bullets are dropped rather than sent to fail validation:
        // an empty row is a user who added one and changed their mind, not an error to report.
        achievements: form.achievements
          .filter((a) => a.text.trim() !== "")
          .map((a, i) => ({ order: i + 1, text: a.text.trim() })),
      });
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
            <TextField label="Company" value={form.company} onChange={text("company")} fullWidth />
            <TextField label="Job title" value={form.title} onChange={text("title")} fullWidth />
          </Stack>
          <TextField
            label="Location"
            value={form.location ?? ""}
            onChange={text("location")}
            fullWidth
          />
          <Stack direction="row" spacing={2}>
            <TextField
              type="date"
              label="Start date"
              InputLabelProps={{ shrink: true }}
              value={form.startDate}
              onChange={text("startDate")}
              fullWidth
            />
            <TextField
              type="date"
              label="End date"
              InputLabelProps={{ shrink: true }}
              value={form.endDate ?? ""}
              onChange={text("endDate")}
              helperText="Leave blank if current"
              fullWidth
            />
          </Stack>
          <TextField
            label="Summary"
            value={form.summary ?? ""}
            onChange={text("summary")}
            fullWidth
            multiline
            minRows={2}
            helperText={SPECIAL_CATEGORY_GUIDANCE}
          />

          <Divider />
          <Typography variant="subtitle2">Achievements</Typography>
          {/* The bullets are the other free-text field an achievement can carry an Art. 9 detail
              into, so the same ask is made once above them rather than repeated per bullet. */}
          <Typography variant="caption" color="text.secondary">
            {SPECIAL_CATEGORY_GUIDANCE}
          </Typography>
          {form.achievements.length === 0 && (
            <Typography variant="body2" color="text.secondary">
              No bullets yet.
            </Typography>
          )}
          {form.achievements.map((a, i) => (
            <Stack key={i} direction="row" spacing={1} alignItems="flex-start">
              <TextField
                label={`Bullet ${i + 1}`}
                value={a.text}
                onChange={(e) => setBulletText(i, e.target.value)}
                fullWidth
                multiline
              />
              <IconButton
                aria-label={`Move bullet ${i + 1} up`}
                disabled={i === 0}
                onClick={() => moveBullet(i, -1)}
              >
                <ArrowUpwardIcon fontSize="small" />
              </IconButton>
              <IconButton
                aria-label={`Move bullet ${i + 1} down`}
                disabled={i === form.achievements.length - 1}
                onClick={() => moveBullet(i, 1)}
              >
                <ArrowDownwardIcon fontSize="small" />
              </IconButton>
              <IconButton
                aria-label={`Remove bullet ${i + 1}`}
                color="error"
                onClick={() => removeBullet(i)}
              >
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Stack>
          ))}
          <Box>
            <Button startIcon={<AddIcon />} onClick={addBullet}>
              Add bullet
            </Button>
          </Box>

          <Divider />
          <Autocomplete
            multiple
            options={options}
            value={selected}
            getOptionLabel={(o) => o.name}
            isOptionEqualToValue={(o, v) => o.id === v.id}
            loading={skillsLoading}
            onChange={(_, v) => setForm((f) => ({ ...f, skillIds: v.map((o) => o.id) }))}
            renderInput={(params) => (
              <TextField {...params} label="Skills" placeholder="Skills used here" />
            )}
          />
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
