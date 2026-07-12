import { Link as RouterLink, useParams } from "react-router-dom";
import {
  Avatar,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import PrintIcon from "@mui/icons-material/Print";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import { useCv } from "../api";
import type { Qualification } from "../types";

function CvSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Box mb={3}>
      <Typography variant="overline" color="primary" fontWeight={700}>
        {title}
      </Typography>
      <Divider sx={{ mb: 1.5 }} />
      {children}
    </Box>
  );
}

function qualLine(q: Qualification): string {
  if (q.type === "Degree") {
    const period = [q.startDate, q.endDate].filter(Boolean).join(" – ");
    return [q.name, q.institution, q.field, period].filter(Boolean).join(" · ");
  }
  const period = q.expiryDate ? `expires ${q.expiryDate}` : q.issueDate ? `issued ${q.issueDate}` : "";
  return [q.name, q.issuer, q.credentialId, period].filter(Boolean).join(" · ");
}

export default function CvPage() {
  const { id = "" } = useParams();
  const { data: cv, isLoading } = useCv(id);

  if (isLoading || !cv) return <CircularProgress />;

  return (
    <Box>
      <Stack direction="row" justifyContent="space-between" mb={2} className="no-print">
        <Button startIcon={<ArrowBackIcon />} component={RouterLink} to={`/employees/${id}`}>
          Back
        </Button>
        <Button variant="contained" startIcon={<PrintIcon />} onClick={() => window.print()}>
          Print / Save PDF
        </Button>
      </Stack>

      <Paper sx={{ p: 5, maxWidth: 820, mx: "auto" }} id="cv-sheet">
        <Stack direction="row" spacing={3} alignItems="center" mb={3}>
          {cv.photoUrl && <Avatar src={cv.photoUrl} sx={{ width: 80, height: 80 }} />}
          <Box flexGrow={1}>
            <Typography variant="h4">{cv.fullName}</Typography>
            <Typography variant="h6" color="text.secondary">
              {cv.title}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {[cv.email, cv.phone, cv.location].filter(Boolean).join("  ·  ")}
            </Typography>
          </Box>
          <Chip
            color={cv.availability.currentCapacityPercent >= 100 ? "success" : "warning"}
            label={`${cv.availability.currentCapacityPercent}% available`}
          />
        </Stack>

        {cv.summary && (
          <CvSection title="Summary">
            <Typography variant="body2">{cv.summary}</Typography>
          </CvSection>
        )}

        {cv.skillGroups.length > 0 && (
          <CvSection title="Skills">
            {cv.skillGroups.map((g) => (
              <Box key={g.category} mb={1}>
                <Typography variant="body2" fontWeight={600}>
                  {g.category}
                </Typography>
                <Stack direction="row" gap={0.5} flexWrap="wrap">
                  {g.skills.map((s) => (
                    <Chip key={s.id} size="small" label={`${s.skillName} (${s.level})`} />
                  ))}
                </Stack>
              </Box>
            ))}
          </CvSection>
        )}

        {cv.experiences.length > 0 && (
          <CvSection title="Experience">
            {cv.experiences.map((x, i) => (
              <Box key={i} mb={2}>
                <Stack direction="row" justifyContent="space-between">
                  <Typography variant="body1" fontWeight={600}>
                    {x.title} · {x.company}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {x.period}
                  </Typography>
                </Stack>
                {x.summary && <Typography variant="body2">{x.summary}</Typography>}
                <ul style={{ margin: "4px 0" }}>
                  {x.achievements.map((a) => (
                    <li key={a.id}>
                      <Typography variant="body2">{a.text}</Typography>
                    </li>
                  ))}
                </ul>
                {x.skills.length > 0 && (
                  <Typography variant="caption" color="text.secondary">
                    {x.skills.join(" · ")}
                  </Typography>
                )}
              </Box>
            ))}
          </CvSection>
        )}

        {cv.education.length > 0 && (
          <CvSection title="Education">
            {cv.education.map((q) => (
              <Typography key={q.id} variant="body2">
                {qualLine(q)}
              </Typography>
            ))}
          </CvSection>
        )}

        {cv.certifications.length > 0 && (
          <CvSection title="Certifications">
            {cv.certifications.map((q) => (
              <Typography key={q.id} variant="body2">
                {qualLine(q)}
              </Typography>
            ))}
          </CvSection>
        )}

        {cv.languages.length > 0 && (
          <CvSection title="Languages">
            <Stack direction="row" gap={0.5} flexWrap="wrap">
              {cv.languages.map((l) => (
                <Chip key={l.id} size="small" variant="outlined" label={`${l.language} (${l.level})`} />
              ))}
            </Stack>
          </CvSection>
        )}
      </Paper>
    </Box>
  );
}
