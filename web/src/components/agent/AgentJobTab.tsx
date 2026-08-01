import { useMemo, useState } from "react";
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import {
  apiErrorMessage,
  useApplyRewrite,
  useCvTailoring,
  useEmployees,
  useMatch,
  type AgentJobRequest,
  type TailoringRewrite,
} from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";
import { PRESET_JDS } from "./presets";

interface FormResult {
  answer: string;
  /** Vetted per-achievement rewrites (CV Tailoring only; empty on the degrade path and for Match). */
  rewrites: TailoringRewrite[];
  latencyMs: number;
  /** The employee the run was submitted for — Apply targets this even if the picker changes later. */
  employeeId: string;
}

// ---- Rewritten bullets (CV Tailoring hybrid contract) ----

/** Rewrites grouped by experienceId, preserving response order. The widget only fetches the
 * employees list (name/title) — not the employee's CV — so a nice "title @ company" header would
 * need a new fetch. We deliberately avoid that and use a neutral positional header instead. */
function groupRewrites(rewrites: TailoringRewrite[]) {
  const groups: { experienceId: string; items: TailoringRewrite[] }[] = [];
  for (const r of rewrites) {
    const group = groups.find((g) => g.experienceId === r.experienceId);
    if (group) group.items.push(r);
    else groups.push({ experienceId: r.experienceId, items: [r] });
  }
  return groups;
}

/** One before → after card with its own Apply mutation, so pending/applied/error state is strictly
 * per card. Apply writes through the Web API with the user's session (never the agent, P1T-62). */
function RewriteCard({ employeeId, rewrite }: { employeeId: string; rewrite: TailoringRewrite }) {
  const apply = useApplyRewrite();
  const r = rewrite;
  return (
    <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }} data-testid={`rewrite-card-${r.achievementId}`}>
      <Typography variant="caption" color="text.secondary">
        Before
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: "anywhere", mb: 1 }}>
        {r.original}
      </Typography>
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <Typography variant="caption" color="text.secondary">
          After
        </Typography>
        <Stack direction="row" alignItems="center" spacing={0.5}>
          {apply.isSuccess ? (
            <Stack direction="row" alignItems="center" spacing={0.5}>
              <CheckCircleOutlineIcon fontSize="small" color="success" />
              <Typography variant="caption" color="success.main" fontWeight={600}>
                Applied
              </Typography>
            </Stack>
          ) : (
            <Button
              size="small"
              disabled={apply.isPending}
              startIcon={
                apply.isPending ? <CircularProgress size={14} color="inherit" /> : undefined
              }
              onClick={() => apply.mutate({ employeeId, ...r })}
            >
              {apply.isPending ? "Applying…" : "Apply"}
            </Button>
          )}
          <Tooltip title="Copy rewritten bullet">
            <IconButton
              size="small"
              aria-label="Copy rewritten bullet"
              onClick={() => void navigator.clipboard.writeText(r.rewritten)}
            >
              <ContentCopyIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Stack>
      </Stack>
      <Typography variant="body2" fontWeight={600} sx={{ overflowWrap: "anywhere" }}>
        {r.rewritten}
      </Typography>
      {apply.isError && (
        <Paper
          elevation={0}
          sx={{ mt: 1, p: 1, bgcolor: "error.light", color: "error.contrastText", borderRadius: 1 }}
        >
          <Typography variant="body2">{apiErrorMessage(apply.error)}</Typography>
        </Paper>
      )}
    </Paper>
  );
}

/** Per-bullet before → after cards, grouped by experience. */
function RewrittenBullets({
  employeeId,
  rewrites,
}: {
  employeeId: string;
  rewrites: TailoringRewrite[];
}) {
  const groups = useMemo(() => groupRewrites(rewrites), [rewrites]);
  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
        Rewritten bullets
      </Typography>
      <Stack spacing={1.5}>
        {groups.map((g, i) => (
          <Stack key={g.experienceId} spacing={1} data-testid={`rewrite-group-${g.experienceId}`}>
            <Typography variant="caption" color="text.secondary" fontWeight={600}>
              Experience {i + 1}
            </Typography>
            {g.items.map((r) => (
              <RewriteCard key={r.achievementId} employeeId={employeeId} rewrite={r} />
            ))}
          </Stack>
        ))}
      </Stack>
    </Box>
  );
}

export function AgentJobForm({
  mode,
  initial,
}: {
  mode: "cv-tailoring" | "match";
  /** Pre-filled employee + JD (e.g. "Run full Match" from a shortlist card). Applied on mount. */
  initial?: AgentJobRequest;
}) {
  const employees = useEmployees();
  const tailoring = useCvTailoring();
  const match = useMatch();
  const run = mode === "cv-tailoring" ? tailoring : match;

  const [employeeId, setEmployeeId] = useState<string | null>(initial?.employeeId ?? null);
  const [jobDescription, setJobDescription] = useState(initial?.jobDescription ?? "");
  const [result, setResult] = useState<FormResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const options = useMemo(
    () =>
      (employees.data ?? []).map((e) => ({
        id: e.id,
        label: `${e.firstName} ${e.lastName} — ${e.title}`,
      })),
    [employees.data],
  );
  const selected = options.find((o) => o.id === employeeId) ?? null;

  const canSubmit = !!employeeId && jobDescription.trim().length > 0 && !run.isPending;

  async function submit() {
    if (!employeeId || !jobDescription.trim() || run.isPending) return;
    setError(null);
    setResult(null);
    const startedAt = performance.now();
    const req: AgentJobRequest = { employeeId, jobDescription: jobDescription.trim() };
    try {
      // Tailoring returns the hybrid contract (answer + rewrites); Match is answer-only.
      const res =
        mode === "cv-tailoring"
          ? await tailoring.mutateAsync(req)
          : { ...(await match.mutateAsync(req)), rewrites: [] };
      setResult({ ...res, latencyMs: performance.now() - startedAt, employeeId: req.employeeId });
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  async function copy() {
    if (result) await navigator.clipboard.writeText(result.answer);
  }

  return (
    <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
      <Stack spacing={1.5}>
        <Autocomplete
          size="small"
          options={options}
          value={selected}
          onChange={(_, v) => setEmployeeId(v?.id ?? null)}
          loading={employees.isLoading}
          isOptionEqualToValue={(o, v) => o.id === v.id}
          renderInput={(params) => <TextField {...params} label="Employee" placeholder="Pick an employee" />}
        />

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

        <Button
          variant="contained"
          disabled={!canSubmit}
          startIcon={run.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />}
          onClick={() => void submit()}
        >
          {run.isPending
            ? mode === "cv-tailoring"
              ? "Tailoring…"
              : "Assessing…"
            : mode === "cv-tailoring"
              ? "Tailor CV"
              : "Assess fit"}
        </Button>

        {error && (
          <Paper
            elevation={0}
            sx={{ p: 1.5, bgcolor: "error.light", color: "error.contrastText", borderRadius: 2 }}
          >
            <Typography variant="body2">{error}</Typography>
          </Paper>
        )}

        {result && (
          <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 0.5 }}>
              <Typography variant="caption" color="text.secondary">
                {(result.latencyMs / 1000).toFixed(1)}s
              </Typography>
              <Tooltip title="Copy answer">
                <IconButton size="small" onClick={() => void copy()} aria-label="Copy answer">
                  <ContentCopyIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </Stack>
            <AgentMarkdown text={result.answer} />
          </Paper>
        )}

        {result && result.rewrites.length > 0 && (
          <RewrittenBullets employeeId={result.employeeId} rewrites={result.rewrites} />
        )}
      </Stack>
    </Box>
  );
}
