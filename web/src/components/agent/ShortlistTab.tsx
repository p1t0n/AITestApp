// Structured results (requirements + ranked candidate cards with evidence), not the markdown pane:
// the endpoint returns a pinned JSON contract composed from the retrieval tool's output.
import { useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Autocomplete,
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
import FilterListIcon from "@mui/icons-material/FilterList";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import HighlightOffIcon from "@mui/icons-material/HighlightOff";
import RequirementChips from "./RequirementChips";
import {
  apiErrorMessage,
  useShortlist,
  useSkills,
  type ShortlistCandidate,
  type ShortlistRequest,
  type ShortlistResponse,
} from "../../api";
import { PRESET_JDS } from "./presets";
import { ErrorNotice } from "../ErrorNotice";

function ShortlistCandidateCard({
  candidate,
  onRunMatch,
}: {
  candidate: ShortlistCandidate;
  onRunMatch: (employeeId: string) => void;
}) {
  const [showEvidence, setShowEvidence] = useState(false);
  const c = candidate;
  return (
    <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }}>
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
        <Stack direction="row" spacing={0.5} flexShrink={0}>
          <Tooltip title="Similarity score">
            <Chip size="small" variant="outlined" label={c.score.toFixed(2)} />
          </Tooltip>
          <Tooltip title="Requirements matched">
            <Chip
              size="small"
              color={c.coverage.matched === c.coverage.total ? "success" : "default"}
              label={`${c.coverage.matched}/${c.coverage.total}`}
            />
          </Tooltip>
        </Stack>
      </Stack>

      <Typography variant="body2" sx={{ mt: 1 }}>
        {c.rationale}
      </Typography>

      <Stack direction="row" justifyContent="space-between" sx={{ mt: 0.5 }}>
        <Button
          size="small"
          onClick={() => setShowEvidence((v) => !v)}
          endIcon={showEvidence ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        >
          Evidence
        </Button>
        <Button size="small" onClick={() => onRunMatch(c.employeeId)}>
          Run full Match
        </Button>
      </Stack>

      <Collapse in={showEvidence} unmountOnExit>
        <Stack spacing={0.75} sx={{ mt: 1 }} data-testid={`evidence-${c.employeeId}`}>
          {c.requirements.map((r, i) => (
            <Stack key={i} direction="row" spacing={1} alignItems="flex-start" data-testid={`evidence-row-${i}`}>
              {r.matched ? (
                <CheckCircleOutlineIcon fontSize="small" color="success" data-testid="matched-icon" />
              ) : (
                <HighlightOffIcon fontSize="small" color="disabled" data-testid="missed-icon" />
              )}
              <Box>
                <Typography variant="body2">{r.text}</Typography>
                {r.snippet && (
                  <Typography variant="caption" color="text.secondary" data-testid="snippet">
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
  onRunMatch: (employeeId: string, jobDescription: string) => void;
}) {
  const shortlist = useShortlist();
  const skills = useSkills();

  const [jobDescription, setJobDescription] = useState("");
  const [showFilters, setShowFilters] = useState(false);
  const [availableOn, setAvailableOn] = useState("");
  const [skillIds, setSkillIds] = useState<string[]>([]);
  const [location, setLocation] = useState("");
  const [minYears, setMinYears] = useState("");
  const [topK, setTopK] = useState("");
  const [result, setResult] = useState<{ data: ShortlistResponse; jobDescription: string } | null>(
    null,
  );
  const [error, setError] = useState<string | null>(null);

  const skillOptions = useMemo(
    () => (skills.data ?? []).map((s) => ({ id: s.id, label: s.name })),
    [skills.data],
  );
  const selectedSkills = skillOptions.filter((o) => skillIds.includes(o.id));

  const canSubmit = jobDescription.trim().length > 0 && !shortlist.isPending;

  async function submit() {
    if (!canSubmit) return;
    setError(null);
    setResult(null);
    const jd = jobDescription.trim();
    // Only the filters the user actually set are sent; the server owns all defaults.
    const req: ShortlistRequest = { jobDescription: jd };
    if (availableOn) req.availableOn = availableOn;
    if (skillIds.length > 0) req.skillIds = skillIds;
    if (location.trim()) req.location = location.trim();
    if (minYears !== "") req.minYears = Number(minYears);
    if (topK !== "") req.topK = Number(topK);
    try {
      setResult({ data: await shortlist.mutateAsync(req), jobDescription: jd });
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  return (
    <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
      <Stack spacing={1.5}>
        <Box>
          <Typography variant="caption" color="text.secondary">
            Job description
          </Typography>
          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mb: 0.5 }}>
            {PRESET_JDS.map((p) => (
              <Chip
                key={p.label}
                label={p.label}
                size="small"
                variant="outlined"
                onClick={() => setJobDescription(p.text)}
              />
            ))}
          </Stack>
          <TextField
            fullWidth
            size="small"
            multiline
            minRows={3}
            maxRows={8}
            placeholder="Paste a job description, or pick a preset above…"
            value={jobDescription}
            onChange={(e) => setJobDescription(e.target.value)}
          />
        </Box>

        <Box>
          <Button
            size="small"
            startIcon={<FilterListIcon />}
            endIcon={showFilters ? <ExpandLessIcon /> : <ExpandMoreIcon />}
            onClick={() => setShowFilters((v) => !v)}
          >
            Filters (optional)
          </Button>
          <Collapse in={showFilters} unmountOnExit>
            <Stack spacing={1.5} sx={{ mt: 1 }}>
              <TextField
                size="small"
                type="date"
                label="Available on"
                InputLabelProps={{ shrink: true }}
                value={availableOn}
                onChange={(e) => setAvailableOn(e.target.value)}
              />
              <Autocomplete
                multiple
                size="small"
                options={skillOptions}
                value={selectedSkills}
                onChange={(_, v) => setSkillIds(v.map((o) => o.id))}
                loading={skills.isLoading}
                isOptionEqualToValue={(o, v) => o.id === v.id}
                renderInput={(params) => (
                  <TextField {...params} label="Skills" placeholder="Any skill" />
                )}
              />
              <TextField
                size="small"
                label="Location"
                placeholder="Any location"
                value={location}
                onChange={(e) => setLocation(e.target.value)}
              />
              <Stack direction="row" spacing={1.5}>
                <TextField
                  size="small"
                  type="number"
                  label="Min years"
                  inputProps={{ min: 0 }}
                  value={minYears}
                  onChange={(e) => setMinYears(e.target.value)}
                />
                <TextField
                  size="small"
                  type="number"
                  label="Top K"
                  placeholder="Server default"
                  inputProps={{ min: 1 }}
                  value={topK}
                  onChange={(e) => setTopK(e.target.value)}
                />
              </Stack>
            </Stack>
          </Collapse>
        </Box>

        <Button
          variant="contained"
          disabled={!canSubmit}
          startIcon={
            shortlist.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />
          }
          onClick={() => void submit()}
        >
          {shortlist.isPending ? "Shortlisting…" : "Build shortlist"}
        </Button>

        <ErrorNotice message={error} />

        {result && (
          <>
            <Box>
              <Typography variant="caption" color="text.secondary">
                How the JD was read
              </Typography>
              <RequirementChips
                requirements={result.data.requirements}
                extraction={result.data.extraction}
              />
            </Box>

            {result.data.candidates.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                No candidates matched this job description. Try loosening the filters.
              </Typography>
            ) : (
              result.data.candidates.map((c) => (
                <ShortlistCandidateCard
                  key={c.employeeId}
                  candidate={c}
                  onRunMatch={(employeeId) => onRunMatch(employeeId, result.jobDescription)}
                />
              ))
            )}
          </>
        )}
      </Stack>
    </Box>
  );
}
