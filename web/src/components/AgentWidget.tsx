import { useMemo, useRef, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Fab,
  IconButton,
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
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import {
  apiErrorMessage,
  useCvTailoring,
  useEmployees,
  useMatch,
  useRosterQa,
  type AgentJobRequest,
} from "../api";

type Mode = "roster" | "cv-tailoring" | "match";

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
  latencyMs: number;
}

function AgentJobForm({ mode }: { mode: "cv-tailoring" | "match" }) {
  const employees = useEmployees();
  const tailoring = useCvTailoring();
  const match = useMatch();
  const run = mode === "cv-tailoring" ? tailoring : match;

  const [employeeId, setEmployeeId] = useState<string | null>(null);
  const [jobDescription, setJobDescription] = useState("");
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
      const { answer } = await run.mutateAsync(req);
      setResult({ answer, latencyMs: performance.now() - startedAt });
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
      </Stack>
    </Box>
  );
}

// ---- Widget shell ----

const TABS: { mode: Mode; label: string }[] = [
  { mode: "roster", label: "Roster Q&A" },
  { mode: "cv-tailoring", label: "Tailor CV" },
  { mode: "match", label: "Match" },
];

export default function AgentWidget() {
  const [open, setOpen] = useState(false);
  const [mode, setMode] = useState<Mode>("roster");

  return (
    <>
      <Fab
        color="primary"
        aria-label="Open the agents assistant"
        onClick={() => setOpen((o) => !o)}
        sx={{ position: "fixed", bottom: 24, right: 24, zIndex: 1300 }}
      >
        {open ? <CloseIcon /> : <SmartToyIcon />}
      </Fab>

      {open && (
        <Paper
          elevation={8}
          sx={{
            position: "fixed",
            bottom: 96,
            right: 24,
            width: 460,
            maxWidth: "calc(100vw - 48px)",
            height: 620,
            maxHeight: "calc(100vh - 140px)",
            display: "flex",
            flexDirection: "column",
            zIndex: 1300,
            borderRadius: 3,
            overflow: "hidden",
          }}
        >
          <Box sx={{ px: 2, py: 1.5, bgcolor: "primary.main", color: "primary.contrastText" }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Stack direction="row" alignItems="center" spacing={1}>
                <SmartToyIcon fontSize="small" />
                <Typography variant="subtitle1">Agents</Typography>
              </Stack>
              <IconButton size="small" onClick={() => setOpen(false)} sx={{ color: "inherit" }}>
                <CloseIcon fontSize="small" />
              </IconButton>
            </Stack>
          </Box>

          <Tabs
            value={mode}
            onChange={(_, v: Mode) => setMode(v)}
            variant="fullWidth"
            sx={{ minHeight: 40, borderBottom: 1, borderColor: "divider" }}
          >
            {TABS.map((t) => (
              <Tab key={t.mode} value={t.mode} label={t.label} sx={{ minHeight: 40, py: 0 }} />
            ))}
          </Tabs>

          {/* Remount per mode so each keeps its own independent state. */}
          {mode === "roster" ? (
            <RosterChat key="roster" />
          ) : (
            <AgentJobForm key={mode} mode={mode} />
          )}
        </Paper>
      )}
    </>
  );
}
