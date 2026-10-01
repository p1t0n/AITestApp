// Structured results (requirements + ranked candidate cards with evidence), not the markdown pane:
// the endpoint returns a pinned JSON contract composed from the retrieval tool's output.
import { useState } from "react";
import { Link as RouterLink } from "react-router";
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Link,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutlined";
import HighlightOffIcon from "@mui/icons-material/HighlightOff";
import RequirementChips from "./RequirementChips";
import {
  apiErrorMessage,
  useShortlist,
  type ShortlistCandidate,
  type ShortlistRequest,
} from "../../api";
import { JdInput } from "./JdInput";
import { JdFilters, useJdFilters } from "./JdFilters";
import { ErrorNotice } from "../ErrorNotice";

function ShortlistCandidateCard({
  candidate,
  onRunMatch,
}: {
  candidate: ShortlistCandidate;
  onRunMatch: (expertId: string) => void;
}) {
  const [showEvidence, setShowEvidence] = useState(false);
  const c = candidate;
  return (
    <Paper sx={{ p: 1.5 }}>
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
        <Stack direction="row" spacing={0.5} sx={{ flexShrink: 0 }}>
          <Tooltip title="Similarity score">
            <Chip variant="outlined" label={c.score.toFixed(2)} />
          </Tooltip>
          <Tooltip title="Requirements matched">
            <Chip
              color={c.coverage.matched === c.coverage.total ? "success" : "default"}
              label={`${c.coverage.matched}/${c.coverage.total}`}
            />
          </Tooltip>
        </Stack>
      </Stack>

      <Typography variant="body2" sx={{ mt: 1 }}>
        {c.rationale}
      </Typography>

      <Stack
        direction="row"
        sx={{ justifyContent: "space-between", mt: 0.5 }}>
        <Button
          onClick={() => setShowEvidence((v) => !v)}
          endIcon={showEvidence ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        >
          Evidence
        </Button>
        <Button onClick={() => onRunMatch(c.expertId)}>
          Run full Match
        </Button>
      </Stack>

      <Collapse in={showEvidence} unmountOnExit>
        <Stack spacing={0.75} sx={{ mt: 1 }} data-testid={`evidence-${c.expertId}`}>
          {c.requirements.map((r, i) => (
            <Stack key={i} direction="row" spacing={1} data-testid={`evidence-row-${i}`} sx={{
              alignItems: "flex-start"
            }}>
              {r.matched ? (
                <CheckCircleOutlineIcon fontSize="small" color="success" data-testid="matched-icon" />
              ) : (
                <HighlightOffIcon fontSize="small" color="disabled" data-testid="missed-icon" />
              )}
              <Box>
                <Typography variant="body2">{r.text}</Typography>
                {r.snippet && (
                  <Typography variant="caption" data-testid="snippet" sx={{
                    color: "text.secondary"
                  }}>
                    {r.snippet}
                  </Typography>
                )}
              </Box>
            </Stack>
          ))}
        </Stack>
      </Collapse>
    </Paper>
  );
}

export function ShortlistPanel({
  onRunMatch,
}: {
  onRunMatch: (expertId: string, jobDescription: string) => void;
}) {
  const shortlist = useShortlist();
  const filters = useJdFilters();

  const [jobDescription, setJobDescription] = useState("");
  const [topK, setTopK] = useState("");

  // The run's result, the JD it ran against and its failure are all on the mutation already, and
  // `mutate` clears them at the start of the next run — mirroring them into state here only added
  // a second copy to keep in step. (BenchTab does mirror, on purpose: it keeps the previous report
  // on screen while a re-run is in flight. This tab does not.)
  const result = shortlist.data;
  const error = shortlist.error ? apiErrorMessage(shortlist.error) : null;

  const canSubmit = jobDescription.trim().length > 0 && !shortlist.isPending;

  function submit() {
    if (!canSubmit) return;
    // Only the filters the user actually set are sent; the server owns all defaults.
    const req: ShortlistRequest = { jobDescription: jobDescription.trim(), ...filters.toRequest() };
    if (topK !== "") req.topK = Number(topK);
    shortlist.mutate(req);
  }

  return (
    <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
      <Stack spacing={1.5}>
        <JdInput value={jobDescription} onChange={setJobDescription} />

        <JdFilters
          filters={filters}
          trailing={
            <TextField
              type="number"
              label="Top K"
              placeholder="Server default"
              value={topK}
              onChange={(e) => setTopK(e.target.value)}
              slotProps={{
                htmlInput: { min: 1 }
              }}
            />
          }
        />

        <Button
          variant="contained"
          disabled={!canSubmit}
          startIcon={
            shortlist.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />
          }
          onClick={submit}
        >
          {shortlist.isPending ? "Shortlisting…" : "Build shortlist"}
        </Button>

        <ErrorNotice message={error} />

        {result && (
          <>
            <Box>
              <Typography variant="caption" sx={{ color: "text.secondary" }}>
                How the JD was read
              </Typography>
              <RequirementChips
                requirements={result.requirements}
                extraction={result.extraction}
              />
            </Box>

            {result.candidates.length === 0 ? (
              <Typography variant="body2" sx={{ color: "text.secondary" }}>
                No candidates matched this job description. Try loosening the filters.
              </Typography>
            ) : (
              result.candidates.map((c) => (
                <ShortlistCandidateCard
                  key={c.expertId}
                  candidate={c}
                  onRunMatch={(expertId) => onRunMatch(expertId, shortlist.variables.jobDescription)}
                />
              ))
            )}
          </>
        )}
      </Stack>
    </Box>
  );
}
