import { useParams } from "react-router-dom";
import {
  Avatar,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Paper,
  Stack,
  ThemeProvider,
  Typography,
} from "@mui/material";
import PrintIcon from "@mui/icons-material/Print";
import DownloadIcon from "@mui/icons-material/Download";
import { useCv, useDownloadCvPdf } from "../api";
import PageHeader, { PageContainer } from "../components/PageHeader";
import { lightTheme } from "../theme";
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
  const downloadPdf = useDownloadCvPdf(id);

  if (isLoading || !cv)
    return (
      <PageContainer width="content">
        <CircularProgress />
      </PageContainer>
    );

  return (
    // Page chrome, not part of the document: printing a CV must not print its own toolbar. The
    // print rule now lives on `PageHeader`'s strip rather than on a `Stack` here — same element,
    // same mechanism, one owner.
    //
    // The title is `CV` and not the person's name on purpose. The sheet below already renders that
    // name as a heading, and it is the *document's* heading — two of them would make the page's own
    // subject ambiguous to a screen reader, and ambiguous to `getByRole("heading", { name })`,
    // which three specs use to prove the sheet rendered.
    <PageHeader
      title="CV"
      backTo={`/employees/${id}`}
      // The sheet caps itself at 820px (§7, frozen); this is the toolbar's own measure, sized to
      // sit close to the sheet's edges rather than float 200px outside them as `lg` did.
      width="content"
      actions={
        <>
          <Button startIcon={<PrintIcon />} onClick={() => window.print()}>
            Print
          </Button>
          {/* Server-rendered PDF (P1T-139) — the same document a headless caller would get,
              rather than whatever the browser's print dialog makes of this page. */}
          <Button
            variant="contained"
            startIcon={<DownloadIcon />}
            disabled={downloadPdf.isPending}
            onClick={() => downloadPdf.mutate()}
          >
            {downloadPdf.isPending ? "Preparing…" : "Download PDF"}
          </Button>
        </>
      }
    >

      {/* The light-lock. The sheet is the *document*, not app chrome: what a client receives
          cannot depend on which Theme Mode the operator happened to be in, so the whole subtree
          renders under the light theme in both modes (P1T-164). The chrome above stays in the
          app's theme — it is print-hidden and never leaves the screen.

          One provider at one boundary rather than a `@media print` colour block, which would
          have to stay exhaustive forever as the sheet grows sections, and would only ever fix
          the paper — leaving the screen lying about what the print will look like.

          `Paper` is what carries it, and that is the load-bearing detail: MUI gives its root both
          `background.paper` *and* `color: text.primary`, so the eight `<Typography>`s in here that
          set no colour of their own — plus the `<li>` markers — inherit the light one. A nested
          provider alone would have re-themed only what names a palette key and left the rest
          inheriting `body`'s near-white `#E6EDF3`: a white-on-white sheet, which is worse than the
          dark one it replaced, because it is invisible rather than merely wrong. */}
      <ThemeProvider theme={lightTheme}>
        {/* On paper the sheet *is* the page: no elevation shadow, no centring margin. These used to
            be a global `#cv-sheet` rule; the id now only marks the sheet for the e2e suite. */}
        <Paper
          variant="elevation"
          elevation={1}
          id="cv-sheet"
          sx={{
            p: 5,
            maxWidth: 820,
            mx: "auto",
            "@media print": { boxShadow: "none", margin: 0 },
          }}
        >
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
                      <Chip key={s.id} label={`${s.skillName} (${s.level})`} />
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
                  <Chip key={l.id} variant="outlined" label={`${l.language} (${l.level})`} />
                ))}
              </Stack>
            </CvSection>
          )}
        </Paper>
      </ThemeProvider>
    </PageHeader>
  );
}
