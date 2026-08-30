import { Box, CircularProgress, LinearProgress, Stack, Typography } from "@mui/material";
import { apiErrorMessage, useUsage, type WindowUsage } from "../../api";
import { ErrorNotice } from "../ErrorNotice";

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

export function UsagePanel() {
  const { data, isLoading, isError, error } = useUsage();
  return (
    <Box sx={{ p: 2, overflowY: "auto" }}>
      {isLoading && <CircularProgress size={24} />}
      <ErrorNotice message={isError ? apiErrorMessage(error) : null} />
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
