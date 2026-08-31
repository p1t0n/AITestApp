// One JD in, a streamed pipeline out (SSE): a live stepper follows the shortlist → match →
// narrative stages, then the terminal report renders recommendation-first with ranked candidate
// cards that drill into the Match and Tailor CV tabs.
import { useEffect, useMemo, useRef, useState } from "react";
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import FilterListIcon from "@mui/icons-material/FilterList";
import {
  apiErrorMessage,
  decideStaffingProposal,
  runStaffing,
  useSkills,
  type StaffingReport,
  type StaffingRequest,
} from "../../api";
import { PRESET_JDS } from "./presets";
import {
  STAFFING_IDLE,
  reduceStaffingStep,
  settleStaffingProgress,
  type StaffingProgress,
} from "./staffingProgress";
import RequirementChips from "./RequirementChips";
import { StaffingStepper } from "./StaffingStepper";
import { StaffingCandidateCard, StaffingRecommendation } from "./StaffingCandidateCard";
import { ProposalInbox } from "./ProposalInbox";
import { ErrorNotice } from "../ErrorNotice";

type DecisionState =
  | { phase: "pending" | "saving" }
  | { phase: "decided"; status: string }
  | { phase: "failed"; message: string };

// The human decision on a run's proposal (P1T-100): the pipeline only proposes — staffing
// outcomes are approved or rejected here, once, by a person. A decision failure keeps the
// buttons live for retry; a conflict (someone else decided first) reads as any other API error.
// Exported for the approval inbox drill-in (P1T-135), which decides from the persisted package.
export function ProposalDecisionCard({
  proposalId,
  onDecided,
}: {
  proposalId: string;
  onDecided?: (status: string) => void;
}) {
  const [state, setState] = useState<DecisionState>({ phase: "pending" });

  async function decide(decision: "approved" | "rejected") {
    setState({ phase: "saving" });
    try {
      const result = await decideStaffingProposal(proposalId, decision);
      setState({ phase: "decided", status: result.status });
      onDecided?.(result.status);
    } catch (err) {
      setState({ phase: "failed", message: apiErrorMessage(err) });
    }
  }

  if (state.phase === "decided") {
    return (
      <Paper variant="well" sx={{ p: 1.5 }} data-testid="proposal-decided">
        <Typography variant="body2" fontWeight={600}>
          Proposal {state.status}
        </Typography>
      </Paper>
    );
  }

  return (
    <Paper variant="well" sx={{ p: 1.5 }} data-testid="proposal-decision">
      <Typography variant="body2" fontWeight={600} sx={{ mb: 1 }}>
        This proposal awaits your decision
      </Typography>
      <ErrorNotice message={state.phase === "failed" ? state.message : null} sx={{ mb: 1 }} />
      <Stack direction="row" spacing={1}>
        <Button
          variant="contained"
          disabled={state.phase === "saving"}
          onClick={() => void decide("approved")}
        >
          Approve
        </Button>
        <Button
          variant="outlined"
          color="inherit"
          disabled={state.phase === "saving"}
          onClick={() => void decide("rejected")}
        >
          Reject
        </Button>
      </Stack>
    </Paper>
  );
}

export function StaffingPanel({
  onOpenInMatch,
  onTailorCv,
}: {
  onOpenInMatch: (employeeId: string, jobDescription: string) => void;
  onTailorCv: (employeeId: string, jobDescription: string) => void;
}) {
  const skills = useSkills();

  const [jobDescription, setJobDescription] = useState("");
  const [showFilters, setShowFilters] = useState(false);
  const [availableOn, setAvailableOn] = useState("");
  const [skillIds, setSkillIds] = useState<string[]>([]);
  const [location, setLocation] = useState("");
  const [minYears, setMinYears] = useState("");
  const [matchTop, setMatchTop] = useState("3");

  const [phase, setPhase] = useState<"idle" | "running" | "done" | "failed">("idle");
  const [progress, setProgress] = useState<StaffingProgress>(STAFFING_IDLE);
  const [report, setReport] = useState<StaffingReport | null>(null);
  const [error, setError] = useState<{ title: string; detail?: string } | null>(null);
  // The JD the current results were produced from — drill-ins prefill this, not the live field.
  const [submittedJd, setSubmittedJd] = useState("");

  // The in-flight stream; aborted on resubmit and on unmount (tab switch / widget close).
  const abortRef = useRef<AbortController | null>(null);
  useEffect(() => () => abortRef.current?.abort(), []);

  const skillOptions = useMemo(
    () => (skills.data ?? []).map((s) => ({ id: s.id, label: s.name })),
    [skills.data],
  );
  const selectedSkills = skillOptions.filter((o) => skillIds.includes(o.id));

  const canSubmit = jobDescription.trim().length > 0 && phase !== "running";

  async function submit() {
    if (!canSubmit) return;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    const jd = jobDescription.trim();
    setError(null);
    setReport(null);
    setProgress(STAFFING_IDLE);
    setPhase("running");
    setSubmittedJd(jd);

    // Only the filters the user actually set are sent; matchTop always is (the selector always
    // shows a concrete value). The server owns all other defaults.
    const req: StaffingRequest = { jobDescription: jd, matchTop: Number(matchTop) };
    if (availableOn) req.availableOn = availableOn;
    if (skillIds.length > 0) req.skillIds = skillIds;
    if (location.trim()) req.location = location.trim();
    if (minYears !== "") req.minYears = Number(minYears);

    let terminal = false;
    try {
      await runStaffing(
        req,
        {
          onStep: (evt) => setProgress((p) => reduceStaffingStep(p, evt)),
          onReport: (r) => {
            terminal = true;
            setProgress(settleStaffingProgress);
            setReport(r);
            setPhase("done");
          },
          onError: (e) => {
            terminal = true;
            setError(e);
            setPhase("failed");
          },
        },
        controller.signal,
      );
      if (!terminal && !controller.signal.aborted) {
        // The server closed the stream without a terminal event — treat it as a dropped run.
        setError({ title: "The stream ended before a report arrived. Try again." });
        setPhase("failed");
      }
    } catch (err) {
      // A deliberate abort (unmount/resubmit) is not an error; anything else is a dropped stream
      // or a pre-stream HTTP failure (the SSE helper's message covers 429 cap bodies).
      if (!controller.signal.aborted) {
        setError({ title: apiErrorMessage(err) });
        setPhase("failed");
      }
    }
  }

  return (
    <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
      <Stack spacing={1.5}>
        {/* The approval inbox (P1T-135): pending proposals decided from their persisted
            handoff packages — nothing here re-runs the pipeline. */}
        <ProposalInbox onOpenInMatch={onOpenInMatch} onTailorCv={onTailorCv} />

        <Box>
          <Typography variant="caption" color="text.secondary">
            Job description
          </Typography>
          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mb: 0.5 }}>
            {PRESET_JDS.map((p) => (
              <Chip
                key={p.label}
                label={p.label}
                variant="outlined"
                onClick={() => setJobDescription(p.text)}
              />
            ))}
          </Stack>
          <TextField
            fullWidth
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
            startIcon={<FilterListIcon />}
            endIcon={showFilters ? <ExpandLessIcon /> : <ExpandMoreIcon />}
            onClick={() => setShowFilters((v) => !v)}
          >
            Filters (optional)
          </Button>
          <Collapse in={showFilters} unmountOnExit>
            <Stack spacing={1.5} sx={{ mt: 1 }}>
              <TextField
                type="date"
                label="Available on"
                InputLabelProps={{ shrink: true }}
                value={availableOn}
                onChange={(e) => setAvailableOn(e.target.value)}
              />
              <Autocomplete
                multiple
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
                label="Location"
                placeholder="Any location"
                value={location}
                onChange={(e) => setLocation(e.target.value)}
              />
              <TextField
                type="number"
                label="Min years"
                inputProps={{ min: 0 }}
                value={minYears}
                onChange={(e) => setMinYears(e.target.value)}
              />
            </Stack>
          </Collapse>
        </Box>

        <TextField
          select
          label="Candidates to match"
          value={matchTop}
          onChange={(e) => setMatchTop(e.target.value)}
          sx={{ width: 180 }}
        >
          {["1", "2", "3", "4", "5"].map((n) => (
            <MenuItem key={n} value={n}>
              {n}
            </MenuItem>
          ))}
        </TextField>

        <Button
          variant="contained"
          disabled={!canSubmit}
          startIcon={
            phase === "running" ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />
          }
          onClick={() => void submit()}
        >
          {phase === "running" ? "Running…" : "Run staffing"}
        </Button>

        {phase !== "idle" && <StaffingStepper progress={progress} done={phase === "done"} />}

        <ErrorNotice message={error?.title} detail={error?.detail} />

        {report && (
          <>
            {report.degraded && (
              <Paper
                variant="well"
                sx={{ p: 1.5, bgcolor: "warning.light", color: "warning.contrastText" }}
                data-testid="staffing-degraded"
              >
                <Typography variant="body2" fontWeight={600}>
                  Partial results
                </Typography>
                {report.notes.map((n, i) => (
                  <Typography key={i} variant="body2">
                    {n}
                  </Typography>
                ))}
              </Paper>
            )}

            <StaffingRecommendation report={report} />

            {report.proposalId && <ProposalDecisionCard proposalId={report.proposalId} />}

            <Box>
              <Typography variant="caption" color="text.secondary">
                How the JD was read
              </Typography>
              <RequirementChips requirements={report.requirements} extraction={report.extraction} />
            </Box>

            {report.candidates.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                No candidates matched this job description. Try loosening the filters.
              </Typography>
            ) : (
              report.candidates.map((c) => (
                <StaffingCandidateCard
                  key={c.employeeId}
                  candidate={c}
                  onOpenInMatch={(employeeId) => onOpenInMatch(employeeId, submittedJd)}
                  onTailorCv={(employeeId) => onTailorCv(employeeId, submittedJd)}
                />
              ))
            )}
          </>
        )}
      </Stack>
    </Box>
  );
}
