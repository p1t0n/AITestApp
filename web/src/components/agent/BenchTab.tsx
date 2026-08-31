// The bench & capability-gap report (P1T-104): one click composes deterministic roster/demand
// stats server-side and renders them as cards, with the model's narrative (or the deterministic
// fallback, see notes) underneath. No numbers on this screen come from model text.
import { useState } from "react";
import { Box, Button, Chip, CircularProgress, Paper, Stack, Typography } from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import { apiErrorMessage, useBenchReport, type BenchReportResponse } from "../../api";
import { AgentMarkdown } from "./AgentMarkdown";
import { ErrorNotice } from "../ErrorNotice";

function StatCard({ label, value }: { label: string; value: string | number }) {
  return (
    <Paper sx={{ p: 1.5, flex: 1, minWidth: 110 }}>
      <Typography variant="h6">{value}</Typography>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
    </Paper>
  );
}

export function BenchPanel() {
  const report = useBenchReport();
  const [result, setResult] = useState<BenchReportResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function run() {
    if (report.isPending) return;
    setError(null);
    try {
      setResult(await report.mutateAsync());
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  const stats = result?.stats;

  return (
    <Box sx={{ flex: 1, overflowY: "auto", p: 1.5 }}>
      <Stack spacing={1.5}>
        <Typography variant="body2" color="text.secondary">
          Bench pressure, staffing demand, and capability gaps — composed from the roster and the
          staffing proposals ledger.
        </Typography>

        <Button
          variant="contained"
          disabled={report.isPending}
          startIcon={report.isPending ? <CircularProgress size={16} color="inherit" /> : <SmartToyIcon />}
          onClick={() => void run()}
        >
          {report.isPending ? "Composing…" : "Generate bench report"}
        </Button>

        <ErrorNotice message={error} />

        {result && result.notes.length > 0 && (
          <Paper
            variant="well"
            sx={{ p: 1.5, bgcolor: "warning.light", color: "warning.contrastText" }}
            data-testid="bench-notes"
          >
            {result.notes.map((n, i) => (
              <Typography key={i} variant="body2">
                {n}
              </Typography>
            ))}
          </Paper>
        )}

        {stats && (
          <>
            <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap" data-testid="bench-stats">
              <StatCard label="Active" value={stats.activeEmployees} />
              <StatCard label="Fully available" value={stats.fullyAvailable} />
              <StatCard label="Partial" value={stats.partiallyAvailable} />
              <StatCard label="Booked" value={stats.fullyBooked} />
              <StatCard label="Avg capacity" value={`${stats.averageCapacityPercent}%`} />
            </Stack>

            {stats.proposals && (
              <Paper sx={{ p: 1.5 }} data-testid="bench-proposals">
                <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
                  Demand (staffing proposals)
                </Typography>
                <Typography variant="body2">
                  {stats.proposals.total} runs — {stats.proposals.pending} pending,{" "}
                  {stats.proposals.approved} approved, {stats.proposals.rejected} rejected
                </Typography>
                {stats.proposals.frequentCandidates.length > 0 && (
                  <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
                    {stats.proposals.frequentCandidates.map((c) => (
                      <Chip key={c.name} label={`${c.name} ×${c.count}`} />
                    ))}
                  </Stack>
                )}
              </Paper>
            )}
          </>
        )}

        {result && (
          <Paper sx={{ p: 1.5 }}>
            <AgentMarkdown text={result.answer} />
          </Paper>
        )}
      </Stack>
    </Box>
  );
}
