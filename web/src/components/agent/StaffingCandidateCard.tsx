import { useState } from "react";
import { Link as RouterLink } from "react-router";
import {
  Box,
  Button,
  Chip,
  Collapse,
  Link,
  Paper,
  Stack,
  Tooltip,
  Typography,
} from "@mui/material";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutlined";
import HighlightOffIcon from "@mui/icons-material/HighlightOff";
import type { StaffingReport, StaffingReportCandidate } from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";

type EvidenceRequirement = StaffingReportCandidate["shortlist"]["requirements"][number];

/** How many matched and how many unmatched requirements a card shows without being opened. */
const INLINE_EVIDENCE = 2;

/** Lines of a CV quote shown inline. A real quote is a whole CV chunk and can run seven lines;
 * the full text is one click away behind "Evidence". */
const INLINE_QUOTE_LINES = 3;

/** One requirement with its verbatim CV quote — the proof the recommendation rests on. */
function EvidenceRow({
  requirement: r,
  clamp = false,
}: {
  requirement: EvidenceRequirement;
  clamp?: boolean;
}) {
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
      {r.matched ? (
        <CheckCircleOutlineIcon fontSize="small" color="success" />
      ) : (
        <HighlightOffIcon fontSize="small" color="disabled" />
      )}
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="body2">{r.text}</Typography>
        {r.snippet && (
          <Typography
            variant="body2"
            component="blockquote"
            sx={{
              m: 0,
              color: "text.secondary",
              fontStyle: "italic",
              ...(clamp && {
                display: "-webkit-box",
                WebkitBoxOrient: "vertical",
                WebkitLineClamp: INLINE_QUOTE_LINES,
                overflow: "hidden",
              }),
              "&::before": { content: '"\\201C"' },
              "&::after": { content: '"\\201D"' },
            }}
          >
            {r.snippet}
          </Typography>
        )}
      </Box>
    </Stack>
  );
}

export function StaffingCandidateCard({
  candidate,
  onOpenInMatch,
  onTailorCv,
}: {
  candidate: StaffingReportCandidate;
  onOpenInMatch: (expertId: string) => void;
  onTailorCv: (expertId: string) => void;
}) {
  const [showMatch, setShowMatch] = useState(false);
  const [showEvidence, setShowEvidence] = useState(false);
  const c = candidate;
  const hasMatchDetails = c.match.status === "completed" && !!c.match.answer;
  const reqs = c.shortlist.requirements;
  const inline = [
    ...reqs.filter((r) => r.matched).slice(0, INLINE_EVIDENCE),
    ...reqs.filter((r) => !r.matched).slice(0, INLINE_EVIDENCE),
  ];
  return (
    <Paper sx={{ p: 1.5 }} data-testid={`staffing-candidate-${c.expertId}`}>
      <Stack
        direction="row"
        spacing={1}
        sx={{ justifyContent: "space-between", alignItems: "flex-start" }}>
        <Box sx={{ minWidth: 0 }}>
          <Link
            component={RouterLink}
            to={`/experts/${c.expertId}`}
            variant="body2"
            sx={{ fontWeight: 600 }}
          >
            {c.name}
          </Link>
          <Typography variant="body2" sx={{ color: "text.secondary" }}>
            {c.title}
          </Typography>
        </Box>
        <Stack
          direction="row"
          spacing={0.5}
          useFlexGap
          sx={{ flexShrink: 0, flexWrap: "wrap", justifyContent: "flex-end" }}>
          <Tooltip title="Requirements matched">
            <Chip
              color={c.shortlist.coverage.matched === c.shortlist.coverage.total ? "success" : "default"}
              label={`${c.shortlist.coverage.matched}/${c.shortlist.coverage.total}`}
            />
          </Tooltip>
          {c.match.status === "completed" && c.match.band && c.match.score != null && (
            <Tooltip title="Match verdict">
              <Chip
                color="primary"
                label={`${c.match.band} · ${c.match.score}`}
                data-testid="staffing-band-chip"
              />
            </Tooltip>
          )}
          {c.match.status === "failed" && (
            <Tooltip title={c.match.error ?? "The match run failed."}>
              <Chip color="error" label="Match failed" />
            </Tooltip>
          )}
          {c.match.status === "skipped" && (
            <Chip variant="outlined" label="Match skipped" sx={{ color: "text.secondary" }} />
          )}
          <Tooltip title="Similarity to the job description">
            <Chip variant="outlined" label={c.shortlist.score.toFixed(2)} />
          </Tooltip>
        </Stack>
      </Stack>

      <Typography variant="body2" sx={{ mt: 1 }}>
        {c.rationale}
      </Typography>

      {!showEvidence && inline.length > 0 && (
        <Stack spacing={0.75} sx={{ mt: 1 }}>
          {inline.map((r, i) => (
            <EvidenceRow key={i} requirement={r} clamp />
          ))}
        </Stack>
      )}

      <Stack
        direction="row"
        useFlexGap
        sx={{ flexWrap: "wrap", columnGap: 0.5, mt: 0.5 }}>
        <Button
          onClick={() => setShowEvidence((v) => !v)}
          endIcon={showEvidence ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        >
          Evidence
        </Button>
        {hasMatchDetails && (
          <Button
            onClick={() => setShowMatch((v) => !v)}
            endIcon={showMatch ? <ExpandLessIcon /> : <ExpandMoreIcon />}
          >
            Match details
          </Button>
        )}
        <Box sx={{ flex: 1 }} />
        <Button onClick={() => onOpenInMatch(c.expertId)}>
          Open in Match
        </Button>
        <Button onClick={() => onTailorCv(c.expertId)}>
          Tailor CV
        </Button>
      </Stack>

      <Collapse in={showEvidence} unmountOnExit>
        <Stack spacing={0.75} sx={{ mt: 1 }} data-testid={`staffing-evidence-${c.expertId}`}>
          {c.shortlist.requirements.map((r, i) => (
            <EvidenceRow key={i} requirement={r} />
          ))}
        </Stack>
      </Collapse>

      {hasMatchDetails && (
        <Collapse in={showMatch} unmountOnExit>
          <Box sx={{ mt: 1, pt: 1, borderTop: 1, borderColor: "divider" }}>
            <AgentMarkdown text={c.match.answer!} />
          </Box>
        </Collapse>
      )}
    </Paper>
  );
}

/** The recommended Expert's name, or null when the run named no one. */
export function recommendedName(report: StaffingReport): string | null {
  const rec = report.recommendation;
  return rec ? (report.candidates.find((c) => c.expertId === rec.expertId)?.name ?? rec.expertId) : null;
}

export function StaffingRecommendation({ report }: { report: StaffingReport }) {
  const rec = report.recommendation;
  const name = recommendedName(report);
  return (
    <Paper
      sx={{ p: 1.5, borderColor: rec ? "primary.main" : "divider" }}
      data-testid="staffing-recommendation"
    >
      <Typography variant="caption" sx={{ color: "text.secondary" }}>
        Recommendation
      </Typography>
      {rec ? (
        <>
          <Box>
            <Link
              component={RouterLink}
              to={`/experts/${rec.expertId}`}
              variant="subtitle2"
              sx={{ fontWeight: 700 }}
            >
              {name}
            </Link>
          </Box>
          <Typography variant="body2" sx={{ mt: 0.5 }}>
            {rec.narrative}
          </Typography>
        </>
      ) : (
        <Typography variant="body2" sx={{ color: "text.secondary" }}>
          No recommendation for this run — see the ranked candidates below.
        </Typography>
      )}
    </Paper>
  );
}
