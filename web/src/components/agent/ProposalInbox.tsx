// The approval inbox + drill-in (P1T-135): pending proposals from the light index, and a drill-in
// that renders the FULL persisted handoff package — the same report components the live run uses
// (recommendation, candidate cards, extraction chips), plus a compact provenance line and the
// degradations. The approver decides from the package alone: nothing here re-runs the pipeline.
import { useState } from "react";
import {
  Box,
  Button,
  CircularProgress,
  Collapse,
  Divider,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import InboxIcon from "@mui/icons-material/Inbox";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import ExpandLessIcon from "@mui/icons-material/ExpandLess";
import {
  apiErrorMessage,
  getStaffingProposal,
  useStaffingProposals,
  type HandoffPackage,
  type StaffingProposalDetail,
  type StaffingProposalSummary,
} from "../../api";
import RequirementChips from "./RequirementChips";
import { StaffingCandidateCard, StaffingRecommendation } from "./StaffingCandidateCard";
import { ProposalDecisionCard } from "./StaffingTab";
import { ErrorNotice } from "../ErrorNotice";

/** One compact line of run provenance: when, by whom, on what model, at what cost, under which
 * caps — the "authorization state travels as provenance" half of the handoff package. */
function ProvenanceLine({ pkg }: { pkg: HandoffPackage }) {
  const model = pkg.slices.map((s) => s.modelId).find((m) => m) ?? "model n/a";
  const tokens = pkg.slices.reduce((sum, s) => sum + s.inputTokens + s.outputTokens, 0);
  const daily = pkg.provenance.capsSnapshotAtStart.find((w) => w.window === "daily");
  const started = new Date(pkg.provenance.startedAt).toLocaleString();
  const caller = pkg.provenance.callerUserId
    ? `by ${pkg.provenance.callerUserId.slice(0, 8)}…`
    : "unattributed";
  return (
    <Typography variant="caption" color="text.secondary" data-testid="proposal-provenance">
      Run {started} {caller} · {model} · {tokens.toLocaleString()} tokens
      {daily ? ` · daily cap ${daily.used.toLocaleString()}/${daily.cap.toLocaleString()} at start` : ""}
    </Typography>
  );
}

/** The drill-in body for a proposal that carries its package: the full report, rendered through
 * the exact components the live staffing run uses. */
function PackageView({
  detail,
  pkg,
  onOpenInMatch,
  onTailorCv,
  onDecided,
}: {
  detail: StaffingProposalDetail;
  pkg: HandoffPackage;
  onOpenInMatch: (employeeId: string, jobDescription: string) => void;
  onTailorCv: (employeeId: string, jobDescription: string) => void;
  onDecided: () => void;
}) {
  const report = pkg.report;
  const jd = detail.jobDescription;
  return (
    <>
      <ProvenanceLine pkg={pkg} />

      {pkg.degradations.length > 0 && (
        <Paper
          variant="well"
          sx={{ p: 1.5, bgcolor: "warning.light", color: "warning.contrastText" }}
          data-testid="proposal-degradations"
        >
          <Typography variant="body2" fontWeight={600}>
            What this run lost
          </Typography>
          {pkg.degradations.map((d, i) => (
            <Typography key={i} variant="body2">
              {d.whatWasLost} — {d.why}
            </Typography>
          ))}
        </Paper>
      )}

      <StaffingRecommendation report={report} />

      {detail.status === "pending" && (
        <ProposalDecisionCard proposalId={detail.id} onDecided={onDecided} />
      )}

      <Box>
        <Typography variant="caption" color="text.secondary">
          How the JD was read
        </Typography>
        <RequirementChips requirements={report.requirements} extraction={report.extraction} />
      </Box>

      {report.candidates.map((c) => (
        <StaffingCandidateCard
          key={c.employeeId}
          candidate={c}
          onOpenInMatch={(employeeId) => onOpenInMatch(employeeId, jd)}
          onTailorCv={(employeeId) => onTailorCv(employeeId, jd)}
        />
      ))}
    </>
  );
}

/** The honest fallback for rows created before the package column existed: the snapshot metadata
 * is all there is, and the drill-in says so instead of pretending. */
function SnapshotOnlyView({
  detail,
  onDecided,
}: {
  detail: StaffingProposalDetail;
  onDecided: () => void;
}) {
  return (
    <>
      <Paper
        variant="well"
        sx={{ p: 1.5 }}
        data-testid="proposal-no-package"
      >
        <Typography variant="body2">
          This proposal predates handoff packages — only the decision snapshot below is available.
        </Typography>
      </Paper>
      {detail.status === "pending" && (
        <ProposalDecisionCard proposalId={detail.id} onDecided={onDecided} />
      )}
      {detail.candidates.map((c) => (
        <Paper key={c.employeeId} sx={{ p: 1.5 }}>
          <Typography variant="subtitle2" fontWeight={700}>
            #{c.rank} {c.name} — {c.title}
          </Typography>
          {c.matchScore != null && (
            <Typography variant="caption" color="text.secondary">
              Match {c.matchScore}/100{c.matchBand ? ` (${c.matchBand})` : ""}
            </Typography>
          )}
          <Typography variant="body2" sx={{ mt: 0.5 }}>
            {c.rationale}
          </Typography>
        </Paper>
      ))}
    </>
  );
}

export function ProposalInbox({
  onOpenInMatch,
  onTailorCv,
}: {
  onOpenInMatch: (employeeId: string, jobDescription: string) => void;
  onTailorCv: (employeeId: string, jobDescription: string) => void;
}) {
  const proposals = useStaffingProposals("pending");
  const [expanded, setExpanded] = useState(false);
  const [detail, setDetail] = useState<StaffingProposalDetail | null>(null);
  const [loadingId, setLoadingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const pending: StaffingProposalSummary[] = proposals.data ?? [];
  if (pending.length === 0 && !detail) {
    return null; // No section when the inbox is empty — the run form stays front and center.
  }

  async function open(id: string) {
    setError(null);
    setLoadingId(id);
    try {
      setDetail(await getStaffingProposal(id));
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setLoadingId(null);
    }
  }

  function closeDetail() {
    setDetail(null);
    void proposals.refetch();
  }

  if (detail) {
    return (
      <Paper sx={{ p: 1.5 }} data-testid="proposal-drill-in">
        <Stack spacing={1.5}>
          <Stack direction="row" alignItems="center" spacing={1}>
            <Button startIcon={<ArrowBackIcon />} onClick={closeDetail}>
              Inbox
            </Button>
            <Typography variant="caption" color="text.secondary" noWrap sx={{ flex: 1 }}>
              {detail.jobDescription}
            </Typography>
          </Stack>
          {detail.package ? (
            <PackageView
              detail={detail}
              pkg={detail.package}
              onOpenInMatch={onOpenInMatch}
              onTailorCv={onTailorCv}
              onDecided={() => void proposals.refetch()}
            />
          ) : (
            <SnapshotOnlyView detail={detail} onDecided={() => void proposals.refetch()} />
          )}
        </Stack>
      </Paper>
    );
  }

  return (
    <Paper sx={{ p: 1.5 }} data-testid="proposal-inbox">
      <Button
        startIcon={<InboxIcon />}
        endIcon={expanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        onClick={() => setExpanded((v) => !v)}
      >
        Pending proposals ({pending.length})
      </Button>
      <Collapse in={expanded} unmountOnExit>
        <Stack spacing={1} sx={{ mt: 1 }} divider={<Divider flexItem />}>
          <ErrorNotice message={error} />
          {pending.map((p) => (
            <Stack
              key={p.id}
              direction="row"
              alignItems="center"
              spacing={1}
              data-testid={`proposal-row-${p.id}`}
            >
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography variant="body2" noWrap>
                  {p.jobDescription}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {new Date(p.createdAt).toLocaleString()} · {p.candidates.length} candidate(s)
                  {p.reportDegraded ? " · partial" : ""}
                </Typography>
              </Box>
              <Button
                variant="outlined"
                disabled={loadingId === p.id}
                startIcon={loadingId === p.id ? <CircularProgress size={14} /> : undefined}
                onClick={() => void open(p.id)}
              >
                Review
              </Button>
            </Stack>
          ))}
        </Stack>
      </Collapse>
    </Paper>
  );
}
