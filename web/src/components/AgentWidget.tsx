import { useMemo, useRef, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Fab,
  IconButton,
  LinearProgress,
  Link,
  Paper,
  Stack,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import CloseIcon from "@mui/icons-material/Close";
import SendIcon from "@mui/icons-material/Send";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import OpenInFullIcon from "@mui/icons-material/OpenInFull";
import CloseFullscreenIcon from "@mui/icons-material/CloseFullscreen";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import FilterListIcon from "@mui/icons-material/FilterList";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import HighlightOffIcon from "@mui/icons-material/HighlightOff";
import type { AgentDock } from "./useAgentDock";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import {
  apiErrorMessage,
  useApplyRewrite,
  useCvTailoring,
  useEmployees,
  useMatch,
  useRosterQa,
  useShortlist,
  useSkills,
  useUsage,
  type AgentJobRequest,
  type ShortlistCandidate,
  type TailoringRewrite,
  type ShortlistRequest,
  type ShortlistResponse,
  type WindowUsage,
} from "../api";

type Mode = "roster" | "cv-tailoring" | "match" | "shortlist" | "usage";

// Matches a GUID anywhere in the text. The agents cite employees by name + id, so we turn those
// ids into links to the employee detail page.
const GUID = /[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}/g;

// Turn bare employee ids into markdown links so they render as navigable links in the answer.
function linkifyGuids(markdown: string): string {
  return markdown.replace(GUID, (id) => `[${id}](/employees/${id})`);
}

/** Renders an agent answer as GitHub-flavoured markdown (headings, lists, tables), with employee
 * ids linkified to their detail page. */
function AgentMarkdown({ text }: { text: string }) {
  return (
    <Box
      sx={{
        fontSize: 14,
        lineHeight: 1.5,
        "& p": { mt: 0, mb: 1 },
        "& ul, & ol": { mt: 0, mb: 1, pl: 2.5 },
        "& h1, & h2, & h3": { fontSize: "1rem", fontWeight: 700, mt: 1.5, mb: 0.5 },
        "& table": { borderCollapse: "collapse", width: "100%", my: 1 },
        "& th, & td": { border: 1, borderColor: "divider", px: 0.75, py: 0.25, textAlign: "left" },
        "& code": { bgcolor: "grey.100", px: 0.5, borderRadius: 0.5 },
      }}
    >
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          a: ({ href, children }) =>
            href?.startsWith("/") ? (
              <Link component={RouterLink} to={href}>
                {children}
              </Link>
            ) : (
              <Link href={href} target="_blank" rel="noopener noreferrer">
                {children}
              </Link>
            ),
        }}
      >
        {linkifyGuids(text)}
      </ReactMarkdown>
    </Box>
  );
}

// ---- Roster chat mode ----

type Role = "user" | "assistant" | "error";
interface Message {
  role: Role;
  text: string;
}

function Bubble({ message }: { message: Message }) {
  const isUser = message.role === "user";
  const isError = message.role === "error";
  return (
    <Box sx={{ display: "flex", justifyContent: isUser ? "flex-end" : "flex-start" }}>
      <Paper
        elevation={0}
        sx={{
          px: 1.5,
          py: 1,
          maxWidth: "85%",
          bgcolor: isUser ? "primary.main" : isError ? "error.light" : "grey.100",
          color: isUser ? "primary.contrastText" : isError ? "error.contrastText" : "text.primary",
          borderRadius: 2,
        }}
      >
        {isUser ? (
          <Box sx={{ whiteSpace: "pre-wrap" }}>{message.text}</Box>
        ) : (
          <AgentMarkdown text={message.text} />
        )}
      </Paper>
    </Box>
  );
}

function RosterChat() {
  const [draft, setDraft] = useState("");
  const [messages, setMessages] = useState<Message[]>([]);
  const ask = useRosterQa();
  const scrollRef = useRef<HTMLDivElement>(null);

  function scrollToEnd() {
    requestAnimationFrame(() => {
      scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
    });
  }

  async function send() {
    const question = draft.trim();
    if (!question || ask.isPending) return;
    setDraft("");
    setMessages((m) => [...m, { role: "user", text: question }]);
    scrollToEnd();
    try {
      const { answer } = await ask.mutateAsync(question);
      setMessages((m) => [...m, { role: "assistant", text: answer }]);
    } catch (err) {
      setMessages((m) => [...m, { role: "error", text: apiErrorMessage(err) }]);
    }
    scrollToEnd();
  }

  return (
    <>
      <Box ref={scrollRef} sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
        <Stack spacing={1}>
          {messages.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ p: 1 }}>
              e.g. "Who knows React and is available this summer?"
            </Typography>
          )}
          {messages.map((m, i) => (
            <Bubble key={i} message={m} />
          ))}
          {ask.isPending && <Bubble message={{ role: "assistant", text: "Thinking…" }} />}
        </Stack>
      </Box>

      <Box sx={{ p: 1, borderTop: 1, borderColor: "divider" }}>
        <Stack direction="row" spacing={1} alignItems="flex-end">
          <TextField
            fullWidth
            size="small"
            multiline
            maxRows={4}
            placeholder="Ask about the roster…"
            value={draft}
            disabled={ask.isPending}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                void send();
              }
            }}
          />
          <IconButton
            color="primary"
            aria-label="Send"
            disabled={ask.isPending || !draft.trim()}
            onClick={() => void send()}
          >
            <SendIcon />
          </IconButton>
        </Stack>
      </Box>
    </>
  );
}

// ---- CV Tailoring / Match form mode ----

const PRESET_JDS: { label: string; text: string }[] = [
  {
    label: "Senior React Engineer",
    text: "Senior Frontend Engineer. 5+ years building production React/TypeScript apps. Strong on component architecture, state management, performance, and accessibility. GraphQL and design-system experience a plus. Some team leadership expected.",
  },
  {
    label: "Backend .NET Engineer",
    text: "Senior Backend Engineer. Deep C#/.NET and ASP.NET Core. PostgreSQL and EF Core, REST API design, distributed systems, and cloud deployment. Experience mentoring and owning services end to end.",
  },
  {
    label: "Platform / DevOps",
    text: "Platform Engineer. Kubernetes, Docker, CI/CD pipelines, infrastructure-as-code (Terraform), observability, and on-call ownership. Cloud (AWS or Azure) and security best practices required.",
  },
];

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

function AgentJobForm({
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

// ---- Shortlist mode ----
// Structured results (requirements + ranked candidate cards with evidence), not the markdown pane:
// the endpoint returns a pinned JSON contract composed from the retrieval tool's output.

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

function ShortlistPanel({
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

        {error && (
          <Paper
            elevation={0}
            sx={{ p: 1.5, bgcolor: "error.light", color: "error.contrastText", borderRadius: 2 }}
          >
            <Typography variant="body2">{error}</Typography>
          </Paper>
        )}

        {result && (
          <>
            <Box>
              <Typography variant="caption" color="text.secondary">
                How the JD was read
              </Typography>
              <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
                {result.data.requirements.map((r) => (
                  <Chip key={r} label={r} size="small" />
                ))}
              </Stack>
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

// ---- Widget shell ----

/** "in 5h" / "in 3d" until the window resets. */
function formatReset(iso: string): string {
  const ms = new Date(iso).getTime() - Date.now();
  if (ms <= 0) return "now";
  const hours = Math.floor(ms / 3_600_000);
  if (hours < 1) return `in ${Math.max(1, Math.floor(ms / 60_000))}m`;
  if (hours < 48) return `in ${hours}h`;
  return `in ${Math.floor(hours / 24)}d`;
}

function UsageBar({ w }: { w: WindowUsage }) {
  const pct = w.cap > 0 ? Math.min(100, (w.used / w.cap) * 100) : 0;
  const color = w.exceeded ? "error" : pct > 80 ? "warning" : "primary";
  return (
    <Box>
      <Stack direction="row" justifyContent="space-between" alignItems="baseline">
        <Typography variant="body2" sx={{ textTransform: "capitalize", fontWeight: 600 }}>
          {w.window}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {w.used.toLocaleString()} / {w.cap.toLocaleString()} · resets {formatReset(w.resetAt)}
        </Typography>
      </Stack>
      <LinearProgress
        variant="determinate"
        value={pct}
        color={color}
        sx={{ height: 8, borderRadius: 1, mt: 0.5 }}
      />
    </Box>
  );
}

function UsagePanel() {
  const { data, isLoading, isError, error } = useUsage();
  return (
    <Box sx={{ p: 2, overflowY: "auto" }}>
      {isLoading && <CircularProgress size={24} />}
      {isError && (
        <Typography color="error" variant="body2">
          {apiErrorMessage(error)}
        </Typography>
      )}
      {data && (
        <Stack spacing={3}>
          <Stack spacing={2}>
            <UsageBar w={data.daily} />
            <UsageBar w={data.weekly} />
            <UsageBar w={data.monthly} />
          </Stack>
          <Box>
            <Typography variant="subtitle2" gutterBottom>
              This month by agent
            </Typography>
            {data.byAgent.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                No usage yet.
              </Typography>
            ) : (
              <Stack spacing={0.5}>
                {data.byAgent.map((a) => (
                  <Stack key={a.agentName} direction="row" justifyContent="space-between">
                    <Typography variant="body2">{a.agentName}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {a.totalTokens.toLocaleString()}
                    </Typography>
                  </Stack>
                ))}
              </Stack>
            )}
          </Box>
        </Stack>
      )}
    </Box>
  );
}

const TABS: { mode: Mode; label: string }[] = [
  { mode: "roster", label: "Roster Q&A" },
  { mode: "cv-tailoring", label: "Tailor CV" },
  { mode: "match", label: "Match" },
  { mode: "shortlist", label: "Shortlist" },
  { mode: "usage", label: "Usage" },
];

export default function AgentWidget({ dock, isNarrow }: { dock: AgentDock; isNarrow: boolean }) {
  const [mode, setMode] = useState<Mode>("roster");

  // "Run full Match" on a shortlist card jumps to the Match tab with the employee + JD pre-filled.
  // Cleared on any manual tab click so a stale prefill never resurfaces later.
  const [matchPrefill, setMatchPrefill] = useState<AgentJobRequest | null>(null);
  function runFullMatch(employeeId: string, jobDescription: string) {
    setMatchPrefill({ employeeId, jobDescription });
    setMode("match");
  }

  // Drag the left edge of the docked sidebar to resize. Width is viewport-minus-cursor, clamped by
  // the hook. Disabled on narrow screens (full-width overlay, no resize).
  const startResize = (e: React.MouseEvent) => {
    e.preventDefault();
    const onMove = (ev: MouseEvent) => dock.setWidth(window.innerWidth - ev.clientX);
    const onUp = () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      document.body.style.userSelect = "";
    };
    document.body.style.userSelect = "none";
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  };

  const dockedWide = dock.docked && !isNarrow;
  const dockedNarrow = dock.docked && isNarrow;

  const panelSx = !dock.docked
    ? {
        bottom: 96,
        right: 24,
        width: 460,
        maxWidth: "calc(100vw - 48px)",
        height: 620,
        maxHeight: "calc(100vh - 140px)",
        borderRadius: 3,
      }
    : dockedNarrow
      ? { inset: 0, width: "100vw", height: "100vh", borderRadius: 0 }
      : { top: 0, right: 0, width: dock.width, height: "100vh", borderRadius: 0, borderLeft: 1, borderColor: "divider" };

  return (
    <>
      {!dock.open && (
        <Fab
          color="primary"
          aria-label="Open the agents assistant"
          onClick={dock.toggleOpen}
          sx={{ position: "fixed", bottom: 24, right: 24, zIndex: 1300 }}
        >
          <SmartToyIcon />
        </Fab>
      )}

      {dock.open && (
        <Paper
          elevation={dockedWide ? 4 : 8}
          square={dock.docked}
          sx={{
            position: "fixed",
            display: "flex",
            flexDirection: "column",
            zIndex: 1300,
            overflow: "hidden",
            ...panelSx,
          }}
        >
          {dockedWide && (
            <Box
              onMouseDown={startResize}
              sx={{
                position: "absolute",
                left: 0,
                top: 0,
                bottom: 0,
                width: 6,
                cursor: "col-resize",
                zIndex: 1,
                "&:hover": { bgcolor: "primary.light" },
              }}
            />
          )}

          <Box sx={{ px: 2, py: 1.5, bgcolor: "primary.main", color: "primary.contrastText" }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Stack direction="row" alignItems="center" spacing={1}>
                <SmartToyIcon fontSize="small" />
                <Typography variant="subtitle1">Agents</Typography>
              </Stack>
              <Stack direction="row" alignItems="center">
                <Tooltip title={dock.docked ? "Float" : "Dock to side"}>
                  <IconButton
                    size="small"
                    onClick={() => dock.setDocked(!dock.docked)}
                    sx={{ color: "inherit" }}
                  >
                    {dock.docked ? (
                      <CloseFullscreenIcon fontSize="small" />
                    ) : (
                      <OpenInFullIcon fontSize="small" />
                    )}
                  </IconButton>
                </Tooltip>
                <IconButton size="small" onClick={dock.close} sx={{ color: "inherit" }}>
                  <CloseIcon fontSize="small" />
                </IconButton>
              </Stack>
            </Stack>
          </Box>

          <Tabs
            value={mode}
            onChange={(_, v: Mode) => {
              setMatchPrefill(null);
              setMode(v);
            }}
            variant="fullWidth"
            sx={{ minHeight: 40, borderBottom: 1, borderColor: "divider" }}
          >
            {TABS.map((t) => (
              <Tab key={t.mode} value={t.mode} label={t.label} sx={{ minHeight: 40, py: 0 }} />
            ))}
          </Tabs>

          {/* Remount per mode so each keeps its own independent state. The Match form also
              remounts per prefill so a new "Run full Match" always lands its values. */}
          {mode === "roster" ? (
            <RosterChat key="roster" />
          ) : mode === "usage" ? (
            <UsagePanel key="usage" />
          ) : mode === "shortlist" ? (
            <ShortlistPanel key="shortlist" onRunMatch={runFullMatch} />
          ) : (
            <AgentJobForm
              key={mode === "match" && matchPrefill ? `match-${matchPrefill.employeeId}` : mode}
              mode={mode}
              initial={mode === "match" ? (matchPrefill ?? undefined) : undefined}
            />
          )}
        </Paper>
      )}
    </>
  );
}
