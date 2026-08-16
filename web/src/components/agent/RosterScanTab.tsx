// Roster Scan (P1T-125): exhaustive async scoring of the (filtered) roster against one JD. The
// tab submits a durable job (202 + an honest calls-vs-RPD estimate), then polls while open —
// progress bar, a paused banner when the quota/cap window parked the job (the normal path, not an
// error), and a partial-results table that fills in as chunks settle. The job keeps running when
// the widget closes.
import { useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  LinearProgress,
  Link,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import FilterListIcon from "@mui/icons-material/FilterList";
import PauseCircleOutlineIcon from "@mui/icons-material/PauseCircleOutline";
import {
  apiErrorMessage,
  useRosterScanJob,
  useSkills,
  useSubmitRosterScan,
  type RosterScanCandidate,
  type RosterScanEstimate,
  type RosterScanRequest,
} from "../../api";
import { PRESET_JDS } from "./presets";

function CandidateRow({
  candidate,
  onRunMatch,
}: {
  candidate: RosterScanCandidate;
  onRunMatch: (employeeId: string) => void;
}) {
  const c = candidate;
  return (
    <Paper variant="outlined" sx={{ p: 1, borderRadius: 2 }} data-testid={`scan-row-${c.employeeId}`}>
      <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
        <Box sx={{ minWidth: 0 }}>
          <Link component={RouterLink} to={`/employees/${c.employeeId}`} variant="body2" fontWeight={600}>
            {c.name}
          </Link>
          <Typography variant="caption" color="text.secondary" sx={{ display: "block" }}>
            {c.title}
          </Typography>
        </Box>
        <Stack direction="row" spacing={0.5} alignItems="center" flexShrink={0}>
          {c.status === "scored" && c.scorable === false ? (
            <Chip size="small" variant="outlined" label="Not scorable" sx={{ color: "text.secondary" }} />
          ) : c.status === "scored" ? (
            <Chip size="small" color="primary" label={`${c.band ?? "?"}${c.score != null ? ` · ${c.score}` : ""}`} />
          ) : c.status === "failed" ? (
            <Chip size="small" color="error" label="Failed" />
          ) : (
            <Chip size="small" variant="outlined" label="Pending" />
          )}
          <Button size="small" onClick={() => onRunMatch(c.employeeId)}>
            Open in Match
          </Button>
        </Stack>
      </Stack>
      {c.rationale && (
        <Typography variant="caption" color="text.secondary">
          {c.rationale}
        </Typography>
      )}
    </Paper>
  );
}

export function RosterScanPanel({
  onOpenInMatch,
}: {
  onOpenInMatch: (employeeId: string, jobDescription: string) => void;
}) {
  const submit = useSubmitRosterScan();
  const skills = useSkills();

  const [jobDescription, setJobDescription] = useState("");
  const [showFilters, setShowFilters] = useState(false);
  const [availableOn, setAvailableOn] = useState("");
  const [skillIds, setSkillIds] = useState<string[]>([]);
  const [location, setLocation] = useState("");
  const [minYears, setMinYears] = useState("");

  const [jobId, setJobId] = useState<string | null>(null);
  const [estimate, setEstimate] = useState<RosterScanEstimate | null>(null);
  const [submittedJd, setSubmittedJd] = useState("");
  const [error, setError] = useState<string | null>(null);

  const job = useRosterScanJob(jobId);

  const skillOptions = useMemo(
    () => (skills.data ?? []).map((s) => ({ id: s.id, label: s.name })),
    [skills.data],
  );
  const selectedSkills = skillOptions.filter((o) => skillIds.includes(o.id));

  const canSubmit = jobDescription.trim().length > 0 && !submit.isPending;

  async function start() {
    if (!canSubmit) return;
    setError(null);
    const jd = jobDescription.trim();
    const req: RosterScanRequest = { jobDescription: jd };
    if (availableOn) req.availableOn = availableOn;
    if (skillIds.length > 0) req.skillIds = skillIds;
    if (location.trim()) req.location = location.trim();
    if (minYears !== "") req.minYears = Number(minYears);
    try {
      const accepted = await submit.mutateAsync(req);
      setJobId(accepted.jobId);
      setEstimate(accepted.estimate);
      setSubmittedJd(jd);
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  const data = job.data;
  const progressPct = data && data.progress.total > 0
    ? (data.progress.settled / data.progress.total) * 100
    : 0;

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
            placeholder="Paste a job description to scan the whole roster against…"
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
                renderInput={(params) => <TextField {...params} label="Skills" />}
              />
              <TextField
                size="small"
                label="Location"
                value={location}
                onChange={(e) => setLocation(e.target.value)}
              />
              <TextField
                size="small"
                type="number"
                label="Min years"
                value={minYears}
                onChange={(e) => setMinYears(e.target.value)}
              />
            </Stack>
          </Collapse>
        </Box>

        <Button
          variant="contained"
          startIcon={submit.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />}
          disabled={!canSubmit}
          onClick={() => void start()}
        >
          Scan the roster
        </Button>

        {error && (
          <Paper
            elevation={0}
            sx={{ p: 1.5, bgcolor: "error.light", color: "error.contrastText", borderRadius: 2 }}
          >
            <Typography variant="body2">{error}</Typography>
          </Paper>
        )}

        {estimate && (
          <Typography variant="caption" color="text.secondary" data-testid="scan-estimate">
            {estimate.candidates} candidate(s) · {estimate.calls} model call(s) against a
            {" "}{estimate.rpdBudget}/day budget. The scan keeps running if you close this panel.
          </Typography>
        )}

        {data && (
          <>
            <Box>
              <Stack direction="row" justifyContent="space-between" sx={{ mb: 0.5 }}>
                <Typography variant="caption" color="text.secondary">
                  {data.state === "completed"
                    ? "Scan complete"
                    : data.state === "failed"
                      ? "Scan failed"
                      : `Scoring ${data.progress.settled}/${data.progress.total}`}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {data.progress.scored} scored · {data.progress.failed} failed
                </Typography>
              </Stack>
              <LinearProgress
                variant="determinate"
                value={progressPct}
                color={data.state === "failed" ? "error" : "primary"}
              />
            </Box>

            {data.state === "paused" && (
              <Paper elevation={0} sx={{ p: 1.5, bgcolor: "warning.light", borderRadius: 2 }} data-testid="scan-paused">
                <Stack direction="row" spacing={1} alignItems="center">
                  <PauseCircleOutlineIcon fontSize="small" />
                  <Typography variant="body2">
                    Paused on the {data.pauseReason === "quota" ? "model quota" : "usage cap"} window
                    {data.resumeAt ? ` — resumes ${new Date(data.resumeAt).toLocaleString()}` : ""}. Partial
                    results below stay available.
                  </Typography>
                </Stack>
              </Paper>
            )}

            {data.state === "failed" && data.failureDetail && (
              <Paper
                elevation={0}
                sx={{ p: 1.5, bgcolor: "error.light", color: "error.contrastText", borderRadius: 2 }}
              >
                <Typography variant="body2">{data.failureDetail}</Typography>
              </Paper>
            )}

            <Stack spacing={0.75}>
              {data.candidates
                .filter((c) => c.status !== "pending")
                .map((c) => (
                  <CandidateRow
                    key={c.employeeId}
                    candidate={c}
                    onRunMatch={(employeeId) => onOpenInMatch(employeeId, submittedJd)}
                  />
                ))}
            </Stack>
          </>
        )}
      </Stack>
    </Box>
  );
}
