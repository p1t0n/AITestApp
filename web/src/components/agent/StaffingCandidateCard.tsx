import { useState } from "react";
import { Link as RouterLink } from "react-router-dom";
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
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import HighlightOffIcon from "@mui/icons-material/HighlightOff";
import type { StaffingReport, StaffingReportCandidate } from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";

export function StaffingCandidateCard({
  candidate,
  onOpenInMatch,
  onTailorCv,
}: {
  candidate: StaffingReportCandidate;
  onOpenInMatch: (employeeId: string) => void;
  onTailorCv: (employeeId: string) => void;
}) {
  const [showMatch, setShowMatch] = useState(false);
  const [showEvidence, setShowEvidence] = useState(false);
  const c = candidate;
  const hasMatchDetails = c.match.status === "completed" && !!c.match.answer;
  return (
    <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }} data-testid={`staffing-candidate-${c.employeeId}`}>
      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={1}>
        <Box sx={{ minWidth: 0 }}>
          <Link
            component={RouterLink}
            to={`/employees/${c.employeeId}`}
            variant="body2"
            fontWeight={600}
          >
            {c.name}
          </Link>
          <Typography variant="body2" color="text.secondary">
            {c.title}
          </Typography>
        </Box>
        <Stack direction="row" spacing={0.5} flexShrink={0} flexWrap="wrap" useFlexGap justifyContent="flex-end">
          <Tooltip title="Similarity score">
            <Chip size="small" variant="outlined" label={c.shortlist.score.toFixed(2)} />
          </Tooltip>
          <Tooltip title="Requirements matched">
            <Chip
              size="small"
              color={c.shortlist.coverage.matched === c.shortlist.coverage.total ? "success" : "default"}
              label={`${c.shortlist.coverage.matched}/${c.shortlist.coverage.total}`}
            />
          </Tooltip>
          {c.match.status === "completed" && c.match.band && c.match.score != null && (
            <Tooltip title="Match verdict">
              <Chip
                size="small"
                color="primary"
                label={`${c.match.band} · ${c.match.score}`}
                data-testid="staffing-band-chip"
              />
            </Tooltip>
          )}
          {c.match.status === "failed" && (
            <Tooltip title={c.match.error ?? "The match run failed."}>
              <Chip size="small" color="error" label="Match failed" />
            </Tooltip>
          )}
          {c.match.status === "skipped" && (
            <Chip size="small" variant="outlined" label="Match skipped" sx={{ color: "text.secondary" }} />
          )}
        </Stack>
      </Stack>

      <Typography variant="body2" sx={{ mt: 1 }}>
        {c.rationale}
      </Typography>

      <Stack direction="row" flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }} columnGap={0.5}>
        <Button
          size="small"
          onClick={() => setShowEvidence((v) => !v)}
          endIcon={showEvidence ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        >
          Evidence
        </Button>
        {hasMatchDetails && (
          <Button
            size="small"
            onClick={() => setShowMatch((v) => !v)}
            endIcon={showMatch ? <ExpandLessIcon /> : <ExpandMoreIcon />}
          >
            Match details
          </Button>
        )}
        <Box sx={{ flex: 1 }} />
        <Button size="small" onClick={() => onOpenInMatch(c.employeeId)}>
          Open in Match
        </Button>
        <Button size="small" onClick={() => onTailorCv(c.employeeId)}>
          Tailor CV
        </Button>
      </Stack>

      <Collapse in={showEvidence} unmountOnExit>
        <Stack spacing={0.75} sx={{ mt: 1 }} data-testid={`staffing-evidence-${c.employeeId}`}>
          {c.shortlist.requirements.map((r, i) => (
            <Stack key={i} direction="row" spacing={1} alignItems="flex-start">
              {r.matched ? (
                <CheckCircleOutlineIcon fontSize="small" color="success" />
              ) : (
                <HighlightOffIcon fontSize="small" color="disabled" />
              )}
              <Box>
                <Typography variant="body2">{r.text}</Typography>
                {r.snippet && (
                  <Typography variant="caption" color="text.secondary">
                    {r.snippet}
                  </Typography>
                )}
              </Box>
            </Stack>
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

export function StaffingRecommendation({ report }: { report: StaffingReport }) {
  const rec = report.recommendation;
  const name = rec
    ? (report.candidates.find((c) => c.employeeId === rec.employeeId)?.name ?? rec.employeeId)
    : null;
  return (
    <Paper
      variant="outlined"
      sx={{ p: 1.5, borderRadius: 2, borderColor: rec ? "primary.main" : "divider" }}
      data-testid="staffing-recommendation"
    >
      <Typography variant="caption" color="text.secondary">
        Recommendation
      </Typography>
      {rec ? (
        <>
          <Box>
            <Link
              component={RouterLink}
              to={`/employees/${rec.employeeId}`}
              variant="subtitle2"
              fontWeight={700}
            >
              {name}
            </Link>
          </Box>
          <Typography variant="body2" sx={{ mt: 0.5 }}>
            {rec.narrative}
          </Typography>
        </>
      ) : (
        <Typography variant="body2" color="text.secondary">
          No recommendation for this run — see the ranked candidates below.
        </Typography>
      )}
    </Paper>
  );
}
