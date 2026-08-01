// Resume ingestion (P1T-96): paste text → the agent stages a DRAFT employee → a human reviews
// (skill proposals, degradation notes, duplicate warning) and promotes or discards. Promotion is
// the publication gate — the roster never shows the draft until it happens.
import { useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Link,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import WarningAmberIcon from "@mui/icons-material/WarningAmber";
import {
  apiErrorMessage,
  useAddEmployeeSkill,
  useCategories,
  useCreateSkill,
  useDeleteEmployee,
  useEmployee,
  usePromoteEmployee,
  useResumeIngestion,
  useSkills,
  useUpdateEmployee,
  type IngestionResponse,
} from "../../api";

/** One proposal's lifecycle. Rejection is local-only: nothing is created, the row just settles. */
type ProposalState =
  | { kind: "pending" }
  | { kind: "mapped"; skillName: string }
  | { kind: "created" }
  | { kind: "rejected" }
  | { kind: "error"; message: string };

function ProposalRow({ name, employeeId }: { name: string; employeeId: string }) {
  const skills = useSkills();
  const categories = useCategories();
  const addSkill = useAddEmployeeSkill(employeeId);
  const createSkill = useCreateSkill();

  const [state, setState] = useState<ProposalState>({ kind: "pending" });
  const [mapTo, setMapTo] = useState<{ id: string; label: string } | null>(null);
  const [categoryId, setCategoryId] = useState("");

  const busy = addSkill.isPending || createSkill.isPending;
  const skillOptions = (skills.data ?? []).map((s) => ({ id: s.id, label: s.name }));

  async function mapToExisting() {
    if (!mapTo) return;
    try {
      await addSkill.mutateAsync({ skillId: mapTo.id, level: "Intermediate", yearsExperience: 0 });
      setState({ kind: "mapped", skillName: mapTo.label });
    } catch (err) {
      setState({ kind: "error", message: apiErrorMessage(err) });
    }
  }

  async function addAsNew() {
    if (!categoryId) return;
    try {
      const skill = await createSkill.mutateAsync({ name, categoryId });
      await addSkill.mutateAsync({ skillId: skill.id, level: "Intermediate", yearsExperience: 0 });
      setState({ kind: "created" });
    } catch (err) {
      setState({ kind: "error", message: apiErrorMessage(err) });
    }
  }

  if (state.kind !== "pending" && state.kind !== "error") {
    return (
      <Stack direction="row" spacing={1} alignItems="center" data-testid={`proposal-${name}`}>
        {state.kind === "rejected" ? (
          <Typography variant="body2" color="text.secondary" sx={{ textDecoration: "line-through" }}>
            {name}
          </Typography>
        ) : (
          <>
            <CheckCircleOutlineIcon fontSize="small" color="success" />
            <Typography variant="body2">
              {name}
              {state.kind === "mapped" ? ` → ${state.skillName}` : " added to the catalog"}
            </Typography>
          </>
        )}
      </Stack>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 1 }} data-testid={`proposal-${name}`}>
      <Typography variant="body2" fontWeight={600}>
        {name}
      </Typography>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
        <Autocomplete
          size="small"
          sx={{ minWidth: 180 }}
          options={skillOptions}
          value={mapTo}
          onChange={(_, v) => setMapTo(v)}
          loading={skills.isLoading}
          isOptionEqualToValue={(o, v) => o.id === v.id}
          renderInput={(params) => <TextField {...params} label="Existing skill" />}
        />
        <Button size="small" disabled={!mapTo || busy} onClick={() => void mapToExisting()}>
          Map to existing
        </Button>
        <TextField
          select
          size="small"
          label="Category"
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          sx={{ minWidth: 150 }}
        >
          {(categories.data ?? []).map((c) => (
            <MenuItem key={c.id} value={c.id}>
              {c.name}
            </MenuItem>
          ))}
        </TextField>
        <Button size="small" disabled={!categoryId || busy} onClick={() => void addAsNew()}>
          Add as new
        </Button>
        <Button size="small" color="inherit" disabled={busy} onClick={() => setState({ kind: "rejected" })}>
          Reject
        </Button>
      </Stack>
      {state.kind === "error" && (
        <Typography variant="caption" color="error">
          {state.message}
        </Typography>
      )}
    </Paper>
  );
}

function DraftReview({ result, onDiscarded }: { result: IngestionResponse; onDiscarded: () => void }) {
  const draft = useEmployee(result.employeeId);
  const promote = usePromoteEmployee();
  const update = useUpdateEmployee(result.employeeId);
  const discard = useDeleteEmployee();

  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);

  const e = draft.data;
  const needsEmail = e != null && e.email.trim() === "" && e.status === "Draft";

  async function promoteNow() {
    if (!e) return;
    setError(null);
    try {
      if (needsEmail) {
        // The gate demands an email; save the human-provided one first, then flip the status.
        await update.mutateAsync({ ...e, email: email.trim() });
      }
      await promote.mutateAsync(e.id);
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  async function discardNow() {
    if (!e) return;
    setError(null);
    try {
      await discard.mutateAsync(e.id);
      onDiscarded();
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  if (draft.isLoading || !e) {
    return <CircularProgress size={24} />;
  }

  const busy = promote.isPending || update.isPending || discard.isPending;
  const promoted = e.status === "Active";
  const counts = result.created;

  return (
    <Stack spacing={1.5} data-testid="ingestion-review">
      <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }}>
        <Stack direction="row" alignItems="center" spacing={1}>
          <Typography variant="subtitle2">
            {e.firstName} {e.lastName}
          </Typography>
          <Chip
            size="small"
            label={e.status}
            color={promoted ? "success" : "warning"}
            data-testid="draft-status-chip"
          />
        </Stack>
        <Typography variant="body2" color="text.secondary">
          {e.title}
          {e.location ? ` — ${e.location}` : ""}
          {e.email ? ` · ${e.email}` : " · no email in the resume"}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          Staged {counts.languages} language(s), {counts.skills} skill(s), {counts.qualifications}{" "}
          qualification(s), {counts.experiences} experience(s).
        </Typography>
      </Paper>

      {result.duplicateWarning && (
        <Paper
          elevation={0}
          sx={{ p: 1.5, bgcolor: "warning.light", color: "warning.contrastText", borderRadius: 2 }}
          data-testid="ingestion-dupe-warning"
        >
          <Stack direction="row" spacing={1} alignItems="center">
            <WarningAmberIcon fontSize="small" />
            <Typography variant="body2">{result.duplicateWarning}</Typography>
          </Stack>
        </Paper>
      )}

      {result.notes.length > 0 && (
        <Paper
          elevation={0}
          sx={{ p: 1.5, bgcolor: "warning.light", color: "warning.contrastText", borderRadius: 2 }}
          data-testid="ingestion-notes"
        >
          <Typography variant="body2" fontWeight={600}>
            Partially staged
          </Typography>
          {result.notes.map((n, i) => (
            <Typography key={i} variant="body2">
              {n}
            </Typography>
          ))}
        </Paper>
      )}

      {e.skills.length > 0 && (
        <Box>
          <Typography variant="caption" color="text.secondary">
            Matched skills
          </Typography>
          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
            {e.skills.map((s) => (
              <Chip key={s.id} size="small" label={s.skillName} />
            ))}
          </Stack>
        </Box>
      )}

      {result.proposals.length > 0 && (
        <Box>
          <Typography variant="caption" color="text.secondary">
            Proposed skills (not in the catalog — your call)
          </Typography>
          <Stack spacing={1} sx={{ mt: 0.5 }}>
            {result.proposals.map((p) => (
              <ProposalRow key={p} name={p} employeeId={e.id} />
            ))}
          </Stack>
        </Box>
      )}

      {error && (
        <Paper
          elevation={0}
          sx={{ p: 1.5, bgcolor: "error.light", color: "error.contrastText", borderRadius: 2 }}
        >
          <Typography variant="body2">{error}</Typography>
        </Paper>
      )}

      {promoted ? (
        <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2, borderColor: "success.main" }}>
          <Typography variant="body2">
            Promoted.{" "}
            <Link component={RouterLink} to={`/employees/${e.id}`}>
              Open the employee page
            </Link>
          </Typography>
        </Paper>
      ) : (
        <Stack direction="row" spacing={1} alignItems="center">
          {needsEmail && (
            <TextField
              size="small"
              label="Email (required to promote)"
              value={email}
              onChange={(ev) => setEmail(ev.target.value)}
            />
          )}
          <Button
            variant="contained"
            disabled={busy || (needsEmail && email.trim() === "")}
            onClick={() => void promoteNow()}
          >
            {promote.isPending || update.isPending ? "Promoting…" : "Promote"}
          </Button>
          <Button color="inherit" disabled={busy} onClick={() => void discardNow()}>
            Discard draft
          </Button>
        </Stack>
      )}
    </Stack>
  );
}

export function IngestionPanel() {
  const ingest = useResumeIngestion();
  const [resumeText, setResumeText] = useState("");
  const [result, setResult] = useState<IngestionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const canSubmit = resumeText.trim().length > 0 && !ingest.isPending;

  async function submit() {
    if (!canSubmit) return;
    setError(null);
    setResult(null);
    try {
      setResult(await ingest.mutateAsync(resumeText.trim()));
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  return (
    <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
      <Stack spacing={1.5}>
        <Box>
          <Typography variant="caption" color="text.secondary">
            Resume text
          </Typography>
          <TextField
            fullWidth
            size="small"
            multiline
            minRows={6}
            maxRows={14}
            placeholder="Paste the raw resume or LinkedIn text…"
            value={resumeText}
            onChange={(e) => setResumeText(e.target.value)}
          />
        </Box>

        <Button
          variant="contained"
          disabled={!canSubmit}
          startIcon={ingest.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />}
          onClick={() => void submit()}
        >
          {ingest.isPending ? "Staging draft…" : "Stage as draft"}
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
          <DraftReview
            result={result}
            onDiscarded={() => {
              setResult(null);
              setResumeText("");
            }}
          />
        )}
      </Stack>
    </Box>
  );
}
