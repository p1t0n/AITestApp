// Roster Scan (P1T-125): exhaustive async scoring of the (filtered) roster against one JD. The
// tab submits a durable job (202 + an honest calls-vs-RPD estimate), then polls while open —
// progress bar, a paused banner when the quota/cap window parked the job (the normal path, not an
// error), and a partial-results table that fills in as chunks settle. The job keeps running when
// the widget closes.
import { useState } from "react";
import { Link as RouterLink } from "react-router";
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  LinearProgress,
  Link,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import PauseCircleOutlineIcon from "@mui/icons-material/PauseCircleOutlined";
import {
  apiErrorMessage,
  useRosterScanJob,
  useSubmitRosterScan,
  type RosterScanCandidate,
  type RosterScanEstimate,
  type RosterScanRequest,
} from "../../api";
import { JdInput } from "./JdInput";
import { JdFilters, useJdFilters } from "./JdFilters";
import { ErrorNotice } from "../ErrorNotice";

function CandidateRow({
  candidate,
  onRunMatch,
}: {
  candidate: RosterScanCandidate;
  onRunMatch: (expertId: string) => void;
}) {
  const c = candidate;
  return (
    <Paper sx={{ p: 1 }} data-testid={`scan-row-${c.expertId}`}>
      <Stack
        direction="row"
        spacing={1}
        sx={{ justifyContent: "space-between", alignItems: "center" }}>
        <Box sx={{ minWidth: 0 }}>
          <Link component={RouterLink} to={`/experts/${c.expertId}`} variant="body2" sx={{
            fontWeight: 600
          }}>
            {c.name}
          </Link>
          <Typography
            variant="caption"
            sx={{ color: "text.secondary", display: "block" }}>
            {c.title}
          </Typography>
        </Box>
        <Stack
          direction="row"
          spacing={0.5}
          sx={{ alignItems: "center", flexShrink: 0 }}>
          {c.status === "scored" && c.scorable === false ? (
            <Chip variant="outlined" label="Not scorable" sx={{ color: "text.secondary" }} />
          ) : c.status === "scored" ? (
            <Chip color="primary" label={`${c.band ?? "?"}${c.score != null ? ` · ${c.score}` : ""}`} />
          ) : c.status === "failed" ? (
            <Chip color="error" label="Failed" />
          ) : (
            <Chip variant="outlined" label="Pending" />
          )}
          <Button onClick={() => onRunMatch(c.expertId)}>
            Open in Match
          </Button>
        </Stack>
      </Stack>
      {c.rationale && (
        <Typography variant="caption" sx={{ color: "text.secondary" }}>
          {c.rationale}
        </Typography>
      )}
    </Paper>
  );
}

export function RosterScanPanel({
  onOpenInMatch,
}: {
  onOpenInMatch: (expertId: string, jobDescription: string) => void;
}) {
  const submit = useSubmitRosterScan();
  const filters = useJdFilters();

  const [jobDescription, setJobDescription] = useState("");

  const [jobId, setJobId] = useState<string | null>(null);
  const [estimate, setEstimate] = useState<RosterScanEstimate | null>(null);
  const [submittedJd, setSubmittedJd] = useState("");
  const [error, setError] = useState<string | null>(null);

  const job = useRosterScanJob(jobId);

  const canSubmit = jobDescription.trim().length > 0 && !submit.isPending;

  async function start() {
    if (!canSubmit) return;
    setError(null);
    const jd = jobDescription.trim();
    const req: RosterScanRequest = { jobDescription: jd, ...filters.toRequest() };
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
        <JdInput
          value={jobDescription}
          onChange={setJobDescription}
          placeholder="Paste a job description to scan the whole roster against…"
        />

        <JdFilters filters={filters} />

        <Button
          variant="contained"
          startIcon={submit.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />}
          disabled={!canSubmit}
          onClick={() => void start()}
        >
          Scan the roster
        </Button>

        <ErrorNotice message={error} />

        {estimate && (
          <Typography variant="caption" data-testid="scan-estimate" sx={{
            color: "text.secondary"
          }}>
            {estimate.candidates} candidate(s) · {estimate.calls} model call(s) against a
            {" "}{estimate.rpdBudget}/day budget. The scan keeps running if you close this panel.
          </Typography>
        )}

        {data && (
          <>
            <Box>
              <Stack
                direction="row"
                sx={{ justifyContent: "space-between", mb: 0.5 }}>
                <Typography variant="caption" sx={{ color: "text.secondary" }}>
                  {data.state === "completed"
                    ? "Scan complete"
                    : data.state === "failed"
                      ? "Scan failed"
                      : `Scoring ${data.progress.settled}/${data.progress.total}`}
                </Typography>
                <Typography variant="caption" sx={{ color: "text.secondary" }}>
                  {data.progress.scored} scored · {data.progress.failed} failed
                </Typography>
              </Stack>
              <LinearProgress
                variant="determinate"
                value={progressPct}
                color={data.state === "failed" ? "error" : "primary"}
              />
            </Box>

            {/* `warning.contrastText`, like the other four warning wells in the dock. This one had
                the fill and not the label colour, so in dark mode it was inheriting the app's
                near-white `text.primary` onto a `#FFD37A` panel — about 1.2:1, which is the
                degradation notice saying a scan paused being the one thing on the panel you cannot
                read. `light` is a saturated mid-step in this palette, not a tint (`tokens.ts`), so
                a fill of it always needs its own label colour. */}
            {data.state === "paused" && (
              <Paper
                variant="well"
                sx={{ p: 1.5, bgcolor: "warning.light", color: "warning.contrastText" }}
                data-testid="scan-paused"
              >
                <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                  <PauseCircleOutlineIcon fontSize="small" />
                  <Typography variant="body2">
                    Paused on the {data.pauseReason === "quota" ? "model quota" : "usage cap"} window
                    {data.resumeAt ? ` — resumes ${new Date(data.resumeAt).toLocaleString()}` : ""}. Partial
                    results below stay available.
                  </Typography>
                </Stack>
              </Paper>
            )}

            <ErrorNotice message={data.state === "failed" ? data.failureDetail : null} />

            <Stack spacing={0.75}>
              {data.candidates
                .filter((c) => c.status !== "pending")
                .map((c) => (
                  <CandidateRow
                    key={c.expertId}
                    candidate={c}
                    onRunMatch={(expertId) => onOpenInMatch(expertId, submittedJd)}
                  />
                ))}
            </Stack>
          </>
        )}
      </Stack>
    </Box>
  );
}
