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
  useExperts,
  useInterviewKit,
  useJdMatch,
  useMatch,
  type AgentJobRequest,
  type InterviewQuestion,
  type JdMatchResponse,
  type JdMatchResult,
  type TailoringRewrite,
} from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";
import { PRESET_JDS } from "./presets";
import { ErrorNotice } from "../ErrorNotice";

interface FormResult {
  answer: string;
  /** Vetted per-achievement rewrites (CV Tailoring only; empty on the degrade path and for Match). */
  rewrites: TailoringRewrite[];
  /** Vetted structured questions (Interview kit only; empty on the degrade path). */
  questions: InterviewQuestion[];
  latencyMs: number;
  /** The expert the run was submitted for — Apply targets this even if the picker changes later. */
  expertId: string;
}

// ---- Rewritten bullets (CV Tailoring hybrid contract) ----

/** Rewrites grouped by experienceId, preserving response order. The widget only fetches the
 * experts list (name/title) — not the expert's CV — so a nice "title @ company" header would
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
function RewriteCard({ expertId, rewrite }: { expertId: string; rewrite: TailoringRewrite }) {
  const apply = useApplyRewrite();
  const r = rewrite;
  return (
    <Paper sx={{ p: 1.5 }} data-testid={`rewrite-card-${r.achievementId}`}>
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
              disabled={apply.isPending}
              startIcon={
                apply.isPending ? <CircularProgress size={14} color="inherit" /> : undefined
              }
              onClick={() => apply.mutate({ expertId, ...r })}
            >
              {apply.isPending ? "Applying…" : "Apply"}
            </Button>
          )}
          <Tooltip title="Copy rewritten bullet">
            <IconButton
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
      <ErrorNotice message={apply.isError ? apiErrorMessage(apply.error) : null} sx={{ mt: 1 }} />
    </Paper>
  );
}

/** Per-bullet before → after cards, grouped by experience. */
function RewrittenBullets({
  expertId,
  rewrites,
}: {
  expertId: string;
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
              <RewriteCard key={r.achievementId} expertId={expertId} rewrite={r} />
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
  mode: "cv-tailoring" | "match" | "interview-kit";
  /** Pre-filled expert + JD (e.g. "Run full Match" from a shortlist card). Applied on mount. */
  initial?: AgentJobRequest;
}) {
  const experts = useExperts();
  const tailoring = useCvTailoring();
  const match = useMatch();
  const interviewKit = useInterviewKit();
  const jdMatch = useJdMatch();
  const run = mode === "cv-tailoring" ? tailoring : mode === "interview-kit" ? interviewKit : match;
  const pending = run.isPending || jdMatch.isPending;

  const [expertId, setExpertId] = useState<string | null>(initial?.expertId ?? null);
  const [jobDescription, setJobDescription] = useState(initial?.jobDescription ?? "");
  const [result, setResult] = useState<FormResult | null>(null);
  // JD-only match results (Match tab with no expert selected, P1T-103).
  const [jdResult, setJdResult] = useState<JdMatchResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const options = useMemo(
    () =>
      (experts.data ?? []).map((e) => ({
        id: e.id,
        label: `${e.firstName} ${e.lastName} — ${e.title}`,
      })),
    [experts.data],
  );
  const selected = options.find((o) => o.id === expertId) ?? null;

  // Match runs without an expert (JD-only mode); the other modes require one.
  const canSubmit =
    (mode === "match" || !!expertId) && jobDescription.trim().length > 0 && !pending;

  async function submit() {
    if (!canSubmit) return;
    setError(null);
    setResult(null);
    setJdResult(null);
    const startedAt = performance.now();
    try {
      if (mode === "match" && !expertId) {
        setJdResult(await jdMatch.mutateAsync({ jobDescription: jobDescription.trim() }));
        return;
      }

      const req: AgentJobRequest = { expertId: expertId!, jobDescription: jobDescription.trim() };
      // Tailoring returns answer + rewrites; the interview kit answer + questions; Match is
      // answer-only. Normalized into one FormResult shape.
      const res =
        mode === "cv-tailoring"
          ? { ...(await tailoring.mutateAsync(req)), questions: [] }
          : mode === "interview-kit"
            ? { ...(await interviewKit.mutateAsync(req)), rewrites: [] }
            : { ...(await match.mutateAsync(req)), rewrites: [], questions: [] };
      setResult({ ...res, latencyMs: performance.now() - startedAt, expertId: req.expertId });
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
          options={options}
          value={selected}
          onChange={(_, v) => setExpertId(v?.id ?? null)}
          loading={experts.isLoading}
          isOptionEqualToValue={(o, v) => o.id === v.id}
          renderInput={(params) => (
            <TextField
              {...params}
              label={mode === "match" ? "Expert (optional)" : "Expert"}
              placeholder={
                mode === "match" ? "Pick an expert, or leave empty to search the roster" : "Pick an expert"
              }
            />
          )}
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

        <Button
          variant="contained"
          disabled={!canSubmit}
          startIcon={pending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />}
          onClick={() => void submit()}
        >
          {pending
            ? mode === "cv-tailoring"
              ? "Tailoring…"
              : mode === "interview-kit"
                ? "Preparing…"
                : "Assessing…"
            : mode === "cv-tailoring"
              ? "Tailor CV"
              : mode === "interview-kit"
                ? "Build interview kit"
                : expertId
                  ? "Assess fit"
                  : "Find matches"}
        </Button>

        <ErrorNotice message={error} />

        {result && (
          <Paper sx={{ p: 1.5 }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 0.5 }}>
              <Typography variant="caption" color="text.secondary">
                {(result.latencyMs / 1000).toFixed(1)}s
              </Typography>
              <Tooltip title="Copy answer">
                <IconButton onClick={() => void copy()} aria-label="Copy answer">
                  <ContentCopyIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </Stack>
            <AgentMarkdown text={result.answer} />
          </Paper>
        )}

        {result && result.rewrites.length > 0 && (
          <RewrittenBullets expertId={result.expertId} rewrites={result.rewrites} />
        )}

        {result && result.questions.length > 0 && <InterviewQuestions questions={result.questions} />}

        {jdResult && <JdMatchResults response={jdResult} />}
      </Stack>
    </Box>
  );
}

/** Ranked JD-only match results (P1T-103): score/band per candidate, failed entries degrade in
 * place with their error instead of hiding. */
function JdMatchResults({ response }: { response: JdMatchResponse }) {
  return (
    <Box>
      <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mb: 1 }}>
        {response.requirements.map((r) => (
          <Chip key={r} label={r} />
        ))}
      </Stack>
      {response.results.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          No candidates matched this job description.
        </Typography>
      ) : (
        <Stack spacing={1}>
          {response.results.map((r) => (
            <JdMatchCard key={r.expertId} result={r} />
          ))}
        </Stack>
      )}
    </Box>
  );
}

function JdMatchCard({ result }: { result: JdMatchResult }) {
  const [open, setOpen] = useState(false);
  return (
    <Paper sx={{ p: 1.5 }} data-testid={`jd-match-${result.expertId}`}>
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <Box>
          <Typography variant="body2" fontWeight={600}>
            {result.name}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {result.title}
          </Typography>
        </Box>
        {result.status === "failed" ? (
          <Chip label="Match failed" color="warning" />
        ) : (
          <Chip
            label={result.score != null ? `${result.score}/100 · ${result.band ?? "?"}` : (result.band ?? "n/a")}
            color={result.band === "Strong" ? "success" : "default"}
          />
        )}
      </Stack>
      {result.error && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
          {result.error}
        </Typography>
      )}
      {result.answer && (
        <>
          <Button onClick={() => setOpen((v) => !v)}>
            {open ? "Hide analysis" : "Show analysis"}
          </Button>
          {open && <AgentMarkdown text={result.answer} />}
        </>
      )}
    </Paper>
  );
}

/** Structured interview questions (Interview kit contract). Evidence renders only when the
 * server verified the quote against the CV — no client-side trust in model text. */
function InterviewQuestions({ questions }: { questions: InterviewQuestion[] }) {
  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
        Questions
      </Typography>
      <Stack spacing={1}>
        {questions.map((q, i) => (
          <Paper key={i} sx={{ p: 1.5 }} data-testid={`interview-question-${i}`}>
            <Typography variant="body2" fontWeight={600} sx={{ overflowWrap: "anywhere" }}>
              {i + 1}. {q.question}
            </Typography>
            {q.probes && (
              <Typography variant="caption" color="text.secondary">
                Probes: {q.probes}
              </Typography>
            )}
            {q.evidence && (
              <Typography
                variant="body2"
                color="text.secondary"
                sx={{ mt: 0.5, fontStyle: "italic", overflowWrap: "anywhere" }}
              >
                “{q.evidence}”
              </Typography>
            )}
          </Paper>
        ))}
      </Stack>
    </Box>
  );
}
