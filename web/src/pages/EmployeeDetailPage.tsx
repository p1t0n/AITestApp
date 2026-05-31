import { useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Grid,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import EditIcon from "@mui/icons-material/Edit";
import DescriptionIcon from "@mui/icons-material/Description";
import {
  useAddAvailability,
  useAddEmployeeSkill,
  useDeleteAvailability,
  useDeleteEmployeeSkill,
  useEmployee,
  useSkills,
  useUpdateEmployee,
} from "../api";
import type { SkillLevel } from "../types";
import EmployeeFormDialog from "./EmployeeFormDialog";

const LEVELS: SkillLevel[] = ["Beginner", "Intermediate", "Advanced", "Expert"];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Paper sx={{ p: 3, mb: 3 }}>
      <Typography variant="h6" gutterBottom>
        {title}
      </Typography>
      <Divider sx={{ mb: 2 }} />
      {children}
    </Paper>
  );
}

export default function EmployeeDetailPage() {
  const { id = "" } = useParams();
  const { data: e, isLoading } = useEmployee(id);
  const { data: catalogSkills } = useSkills();
  const update = useUpdateEmployee(id);
  const addSkill = useAddEmployeeSkill(id);
  const delSkill = useDeleteEmployeeSkill(id);
  const addAvail = useAddAvailability(id);
  const delAvail = useDeleteAvailability(id);

  const [editOpen, setEditOpen] = useState(false);
  const [skillId, setSkillId] = useState("");
  const [level, setLevel] = useState<SkillLevel>("Intermediate");
  const [years, setYears] = useState(1);
  const [effectiveFrom, setEffectiveFrom] = useState("");
  const [capacity, setCapacity] = useState(100);

  if (isLoading || !e) return <CircularProgress />;

  return (
    <Box>
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
        <Box>
          <Typography variant="h4">
            {e.firstName} {e.lastName}
          </Typography>
          <Typography color="text.secondary">{e.title}</Typography>
        </Box>
        <Stack direction="row" spacing={1}>
          <Button startIcon={<EditIcon />} onClick={() => setEditOpen(true)}>
            Edit
          </Button>
          <Button
            variant="contained"
            startIcon={<DescriptionIcon />}
            component={RouterLink}
            to={`/employees/${id}/cv`}
          >
            View CV
          </Button>
        </Stack>
      </Stack>

      <Section title="Profile">
        <Grid container spacing={1}>
          <Grid item xs={6}><b>Email:</b> {e.email}</Grid>
          <Grid item xs={6}><b>Phone:</b> {e.phone ?? "—"}</Grid>
          <Grid item xs={6}><b>Location:</b> {e.location ?? "—"}</Grid>
          <Grid item xs={6}><b>Current capacity:</b> {e.currentCapacityPercent}%</Grid>
          <Grid item xs={12} sx={{ mt: 1 }}>{e.summary ?? "No summary."}</Grid>
        </Grid>
      </Section>

      <Section title="Availability schedule">
        <Stack spacing={1} mb={2}>
          {e.availabilityEntries.length === 0 && <Typography color="text.secondary">No entries.</Typography>}
          {e.availabilityEntries.map((a) => (
            <Stack key={a.id} direction="row" alignItems="center" spacing={2}>
              <Chip label={`${a.capacityPercent}%`} size="small" />
              <Typography>from {a.effectiveFrom}</Typography>
              <IconButton size="small" color="error" onClick={() => delAvail.mutate(a.id)}>
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Stack>
          ))}
        </Stack>
        <Stack direction="row" spacing={2} alignItems="center">
          <TextField
            type="date"
            label="Effective from"
            InputLabelProps={{ shrink: true }}
            value={effectiveFrom}
            onChange={(ev) => setEffectiveFrom(ev.target.value)}
          />
          <TextField
            type="number"
            label="Capacity %"
            value={capacity}
            onChange={(ev) => setCapacity(Number(ev.target.value))}
            sx={{ width: 120 }}
          />
          <Button
            disabled={!effectiveFrom}
            onClick={() => addAvail.mutate({ effectiveFrom, capacityPercent: capacity })}
          >
            Add
          </Button>
        </Stack>
      </Section>

      <Section title="Skills">
        <Stack direction="row" flexWrap="wrap" gap={1} mb={2}>
          {e.skills.map((s) => (
            <Chip
              key={s.id}
              label={`${s.skillName} · ${s.level} · ${s.yearsExperience}y`}
              onDelete={() => delSkill.mutate(s.id)}
            />
          ))}
          {e.skills.length === 0 && <Typography color="text.secondary">No skills.</Typography>}
        </Stack>
        <Stack direction="row" spacing={2} alignItems="center">
          <TextField
            select
            label="Skill"
            value={skillId}
            onChange={(ev) => setSkillId(ev.target.value)}
            sx={{ minWidth: 200 }}
          >
            {catalogSkills?.map((s) => (
              <MenuItem key={s.id} value={s.id}>
                {s.name}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            select
            label="Level"
            value={level}
            onChange={(ev) => setLevel(ev.target.value as SkillLevel)}
            sx={{ minWidth: 140 }}
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
            value={years}
            onChange={(ev) => setYears(Number(ev.target.value))}
            sx={{ width: 100 }}
          />
          <Button
            disabled={!skillId}
            onClick={() =>
              addSkill.mutate({ skillId, level, yearsExperience: years }, { onSuccess: () => setSkillId("") })
            }
          >
            Add
          </Button>
        </Stack>
      </Section>

      <Section title="Experience">
        {e.experiences.length === 0 && <Typography color="text.secondary">No experience recorded.</Typography>}
        {e.experiences.map((x) => (
          <Box key={x.id} mb={2}>
            <Typography fontWeight={600}>
              {x.title} · {x.company}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {x.startDate} – {x.endDate ?? "Present"} {x.location ? `· ${x.location}` : ""}
            </Typography>
            {x.summary && <Typography variant="body2">{x.summary}</Typography>}
            <ul style={{ marginTop: 4 }}>
              {x.achievements.map((a) => (
                <li key={a.id}>{a.text}</li>
              ))}
            </ul>
            <Stack direction="row" gap={0.5} flexWrap="wrap">
              {x.skills.map((s) => (
                <Chip key={s.id} size="small" variant="outlined" label={s.skillName} />
              ))}
            </Stack>
          </Box>
        ))}
      </Section>

      <Section title="Qualifications">
        {e.qualifications.length === 0 && <Typography color="text.secondary">None.</Typography>}
        {e.qualifications.map((q) => (
          <Box key={q.id} mb={1}>
            <Chip size="small" label={q.type} sx={{ mr: 1 }} />
            <b>{q.name}</b>
            {q.institution ? ` — ${q.institution}` : ""}
            {q.issuer ? ` — ${q.issuer}` : ""}
          </Box>
        ))}
      </Section>

      <Section title="Languages">
        <Stack direction="row" gap={1} flexWrap="wrap">
          {e.spokenLanguages.map((l) => (
            <Chip key={l.id} label={`${l.language} · ${l.level}`} />
          ))}
          {e.spokenLanguages.length === 0 && <Typography color="text.secondary">None.</Typography>}
        </Stack>
      </Section>

      <EmployeeFormDialog
        open={editOpen}
        title="Edit employee"
        initial={{
          firstName: e.firstName,
          lastName: e.lastName,
          title: e.title,
          email: e.email,
          phone: e.phone,
          location: e.location,
          summary: e.summary,
          photoUrl: e.photoUrl,
        }}
        onClose={() => setEditOpen(false)}
        onSave={(dto) => update.mutateAsync(dto)}
      />
    </Box>
  );
}
