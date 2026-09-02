import { useState } from "react";
import {
  Box,
  Button,
  Chip,
  Divider,
  IconButton,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/Delete";
import EditIcon from "@mui/icons-material/Edit";
import {
  useAddAvailability,
  useAddExpertSkill,
  useAddExperience,
  useAddLanguage,
  useAddQualification,
  useDeleteAvailability,
  useDeleteExpertSkill,
  useDeleteExperience,
  useDeleteLanguage,
  useDeleteQualification,
  useUpdateAvailability,
  useUpdateExpertSkill,
  useUpdateExperience,
  useUpdateLanguage,
  useUpdateQualification,
} from "../api";
import type {
  AvailabilityEntry,
  ExpertDetail,
  ExpertSkill,
  Experience,
  Qualification,
  SaveAvailabilityEntry,
  SaveExpertSkill,
  SaveExperience,
  SaveQualification,
  SaveSpokenLanguage,
  SpokenLanguage,
} from "../types";
import AvailabilityFormDialog from "../pages/AvailabilityFormDialog";
import ExpertSkillFormDialog from "../pages/ExpertSkillFormDialog";
import ExperienceFormDialog from "../pages/ExperienceFormDialog";
import LanguageFormDialog from "../pages/LanguageFormDialog";
import QualificationFormDialog from "../pages/QualificationFormDialog";

/**
 * One titled block of a record, with its own action. Exported because both pages that show a record
 * use it and a second copy would drift in padding alone.
 */
export function Section({
  title,
  action,
  children,
}: {
  title: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <Paper sx={{ p: 3, mb: 3 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h6" gutterBottom>
          {title}
        </Typography>
        {action}
      </Stack>
      <Divider sx={{ mb: 2 }} />
      {children}
    </Paper>
  );
}

/**
 * Each child dialog is rendered only while its row is being edited, so mounting is what loads the
 * form state. The form dialogs seed themselves from `initial` on first render only, and a dialog
 * kept mounted across two different rows would show the first row's values for the second.
 */
type EditTarget<T> = { id?: string; initial?: Partial<T> } | null;

/**
 * The skill row carries one thing the payload does not: the catalog name to show while the picker
 * is locked.
 */
type SkillEditTarget = (EditTarget<SaveExpertSkill> & { skillName?: string }) | null;

function toSaveLanguage(l: SpokenLanguage): SaveSpokenLanguage {
  return { language: l.language, level: l.level };
}

function toSaveAvailability(a: AvailabilityEntry): SaveAvailabilityEntry {
  return { effectiveFrom: a.effectiveFrom, capacityPercent: a.capacityPercent };
}

function toSaveExpertSkill(s: ExpertSkill): SaveExpertSkill {
  return { skillId: s.skillId, level: s.level, yearsExperience: s.yearsExperience };
}

function toSaveQualification(q: Qualification): SaveQualification {
  const { id: _id, ...rest } = q;
  return rest;
}

function toSaveExperience(x: Experience): SaveExperience {
  return {
    company: x.company,
    title: x.title,
    location: x.location,
    startDate: x.startDate,
    endDate: x.endDate,
    summary: x.summary,
    achievements: x.achievements.map((a) => ({ order: a.order, text: a.text })),
    skillIds: x.skills.map((s) => s.skillId),
  };
}

/**
 * The five child collections of a record — availability, skills, experience, qualifications,
 * languages — with their add, edit and delete affordances.
 *
 * <p><b>Genuinely shared, by both pages that edit a record</b> (P1T-190): the Service Manager's
 * expert page and the Expert's own My CV. What was rejected is the other shape — one page serving
 * two roles with controls hidden by role — because "hidden for Experts" is one `sx` prop away from
 * not hidden, and that is how a page grows holes. The divergence lives in what each page
 * <em>offers around</em> these sections, not in what a shared component conditionally suppresses.
 * Everything here is identical for both, because the server's ownership scope has already decided
 * which record the caller can reach.</p>
 *
 * <p>The skill picker reads the catalog and cannot add to it. No proposal affordance exists, for
 * two reasons: it would create a third human queue after claim approvals and contested scores, and
 * the catalog is a curated taxonomy that semantic search and shortlist ranking depend on.</p>
 */
export default function ExpertRecordSections({
  expertId,
  expert: e,
}: {
  expertId: string;
  expert: ExpertDetail;
}) {
  const addSkill = useAddExpertSkill(expertId);
  const updateSkill = useUpdateExpertSkill(expertId);
  const delSkill = useDeleteExpertSkill(expertId);
  const addAvail = useAddAvailability(expertId);
  const updateAvail = useUpdateAvailability(expertId);
  const delAvail = useDeleteAvailability(expertId);
  const addLanguage = useAddLanguage(expertId);
  const updateLanguage = useUpdateLanguage(expertId);
  const delLanguage = useDeleteLanguage(expertId);
  const addQualification = useAddQualification(expertId);
  const updateQualification = useUpdateQualification(expertId);
  const delQualification = useDeleteQualification(expertId);
  const addExperience = useAddExperience(expertId);
  const updateExperience = useUpdateExperience(expertId);
  const delExperience = useDeleteExperience(expertId);

  const [languageEdit, setLanguageEdit] = useState<EditTarget<SaveSpokenLanguage>>(null);
  const [qualificationEdit, setQualificationEdit] = useState<EditTarget<SaveQualification>>(null);
  const [experienceEdit, setExperienceEdit] = useState<EditTarget<SaveExperience>>(null);
  const [availabilityEdit, setAvailabilityEdit] = useState<EditTarget<SaveAvailabilityEntry>>(null);
  const [skillEdit, setSkillEdit] = useState<SkillEditTarget>(null);

  return (
    <>
    <Section
      title="Availability schedule"
      action={
        <Button startIcon={<AddIcon />} onClick={() => setAvailabilityEdit({ initial: undefined })}>
          Add availability
        </Button>
      }
    >
      <Stack spacing={1}>
        {e.availabilityEntries.length === 0 && <Typography color="text.secondary">No entries.</Typography>}
        {e.availabilityEntries.map((a) => (
          <Stack key={a.id} direction="row" alignItems="center" spacing={2}>
            <Chip label={`${a.capacityPercent}%`} />
            <Typography>from {a.effectiveFrom}</Typography>
            <IconButton
              aria-label={`Edit availability from ${a.effectiveFrom}`}
              onClick={() => setAvailabilityEdit({ id: a.id, initial: toSaveAvailability(a) })}
            >
              <EditIcon fontSize="small" />
            </IconButton>
            <IconButton
              color="error"
              aria-label={`Delete availability from ${a.effectiveFrom}`}
              onClick={() => delAvail.mutate(a.id)}
            >
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Stack>
        ))}
      </Stack>
    </Section>

    <Section
      title="Skills"
      action={
        <Button startIcon={<AddIcon />} onClick={() => setSkillEdit({ initial: undefined })}>
          Add skill
        </Button>
      }
    >
      <Stack direction="row" flexWrap="wrap" gap={1}>
        {e.skills.map((s) => (
          <Chip
            key={s.id}
            label={`${s.skillName} · ${s.level} · ${s.yearsExperience}y`}
            onClick={() =>
              setSkillEdit({ id: s.id, initial: toSaveExpertSkill(s), skillName: s.skillName })
            }
            onDelete={() => delSkill.mutate(s.id)}
          />
        ))}
        {e.skills.length === 0 && <Typography color="text.secondary">No skills.</Typography>}
      </Stack>
    </Section>

    <Section
      title="Experience"
      action={
        <Button
          startIcon={<AddIcon />}
          onClick={() => setExperienceEdit({ initial: undefined })}
        >
          Add experience
        </Button>
      }
    >
      {e.experiences.length === 0 && <Typography color="text.secondary">No experience recorded.</Typography>}
      {e.experiences.map((x) => (
        <Box key={x.id} mb={2}>
          <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
            <Box>
              <Typography fontWeight={600}>
                {x.title} · {x.company}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {x.startDate} – {x.endDate ?? "Present"} {x.location ? `· ${x.location}` : ""}
              </Typography>
            </Box>
            <Stack direction="row">
              <IconButton
                aria-label={`Edit ${x.title} at ${x.company}`}
                onClick={() => setExperienceEdit({ id: x.id, initial: toSaveExperience(x) })}
              >
                <EditIcon fontSize="small" />
              </IconButton>
              <IconButton
                color="error"
                aria-label={`Delete ${x.title} at ${x.company}`}
                onClick={() => delExperience.mutate(x.id)}
              >
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Stack>
          </Stack>
          {x.summary && <Typography variant="body2">{x.summary}</Typography>}
          <ul style={{ marginTop: 4 }}>
            {x.achievements.map((a) => (
              <li key={a.id}>{a.text}</li>
            ))}
          </ul>
          <Stack direction="row" gap={0.5} flexWrap="wrap">
            {x.skills.map((s) => (
              <Chip key={s.id} variant="outlined" label={s.skillName} />
            ))}
          </Stack>
        </Box>
      ))}
    </Section>

    <Section
      title="Qualifications"
      action={
        <Button
          startIcon={<AddIcon />}
          onClick={() => setQualificationEdit({ initial: undefined })}
        >
          Add qualification
        </Button>
      }
    >
      {e.qualifications.length === 0 && <Typography color="text.secondary">None.</Typography>}
      {e.qualifications.map((q) => (
        <Stack key={q.id} direction="row" alignItems="center" spacing={1} mb={1}>
          <Chip label={q.type} />
          <Typography>
            <b>{q.name}</b>
            {q.institution ? ` — ${q.institution}` : ""}
            {q.issuer ? ` — ${q.issuer}` : ""}
          </Typography>
          <IconButton
            aria-label={`Edit ${q.name}`}
            onClick={() => setQualificationEdit({ id: q.id, initial: toSaveQualification(q) })}
          >
            <EditIcon fontSize="small" />
          </IconButton>
          <IconButton
            color="error"
            aria-label={`Delete ${q.name}`}
            onClick={() => delQualification.mutate(q.id)}
          >
            <DeleteIcon fontSize="small" />
          </IconButton>
        </Stack>
      ))}
    </Section>

    <Section
      title="Languages"
      action={
        <Button startIcon={<AddIcon />} onClick={() => setLanguageEdit({ initial: undefined })}>
          Add language
        </Button>
      }
    >
      <Stack direction="row" gap={1} flexWrap="wrap">
        {e.spokenLanguages.map((l) => (
          <Chip
            key={l.id}
            label={`${l.language} · ${l.level}`}
            onClick={() => setLanguageEdit({ id: l.id, initial: toSaveLanguage(l) })}
            onDelete={() => delLanguage.mutate(l.id)}
          />
        ))}
        {e.spokenLanguages.length === 0 && <Typography color="text.secondary">None.</Typography>}
      </Stack>
    </Section>

    {languageEdit && (
      <LanguageFormDialog
        open
        title={languageEdit.id ? "Edit language" : "Add language"}
        initial={languageEdit.initial}
        onClose={() => setLanguageEdit(null)}
        onSave={(dto) =>
          languageEdit.id
            ? updateLanguage.mutateAsync({ id: languageEdit.id, ...dto })
            : addLanguage.mutateAsync(dto)
        }
      />
    )}

    {qualificationEdit && (
      <QualificationFormDialog
        open
        title={qualificationEdit.id ? "Edit qualification" : "Add qualification"}
        initial={qualificationEdit.initial}
        onClose={() => setQualificationEdit(null)}
        onSave={(dto) =>
          qualificationEdit.id
            ? updateQualification.mutateAsync({ id: qualificationEdit.id, ...dto })
            : addQualification.mutateAsync(dto)
        }
      />
    )}

    {availabilityEdit && (
      <AvailabilityFormDialog
        open
        title={availabilityEdit.id ? "Edit availability" : "Add availability"}
        initial={availabilityEdit.initial}
        onClose={() => setAvailabilityEdit(null)}
        onSave={(dto) =>
          availabilityEdit.id
            ? updateAvail.mutateAsync({ id: availabilityEdit.id, ...dto })
            : addAvail.mutateAsync(dto)
        }
      />
    )}

    {skillEdit && (
      <ExpertSkillFormDialog
        open
        title={skillEdit.id ? "Edit skill" : "Add skill"}
        initial={skillEdit.initial}
        lockedSkillName={skillEdit.skillName}
        onClose={() => setSkillEdit(null)}
        onSave={(dto) =>
          skillEdit.id
            ? updateSkill.mutateAsync({ id: skillEdit.id, ...dto })
            : addSkill.mutateAsync(dto)
        }
      />
    )}

    {experienceEdit && (
      <ExperienceFormDialog
        open
        title={experienceEdit.id ? "Edit experience" : "Add experience"}
        initial={experienceEdit.initial}
        onClose={() => setExperienceEdit(null)}
        onSave={(dto) =>
          experienceEdit.id
            ? updateExperience.mutateAsync({ id: experienceEdit.id, ...dto })
            : addExperience.mutateAsync(dto)
        }
      />
    )}
    </>
  );
}
