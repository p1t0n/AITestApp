import { Box, CircularProgress, Paper, Stack, Typography } from "@mui/material";
import CheckCircleOutlineIcon from "@mui/icons-material/CheckCircleOutline";
import RadioButtonUncheckedIcon from "@mui/icons-material/RadioButtonUnchecked";
import WarningAmberIcon from "@mui/icons-material/WarningAmber";
import type { StaffingProgress, StaffingStageView } from "./staffingProgress";

function StaffingStepRow({
  id,
  label,
  state,
  error,
  children,
}: {
  id: string;
  label: string;
  state: StaffingStageView;
  error?: string | null;
  children?: React.ReactNode;
}) {
  return (
    <Box data-testid={`staffing-step-${id}`}>
      <Stack direction="row" spacing={1} alignItems="center">
        {state === "done" ? (
          <CheckCircleOutlineIcon fontSize="small" color="success" />
        ) : state === "active" ? (
          <CircularProgress size={16} />
        ) : state === "failed" ? (
          <WarningAmberIcon fontSize="small" color="warning" />
        ) : (
          <RadioButtonUncheckedIcon fontSize="small" color="disabled" />
        )}
        <Typography
          variant="body2"
          color={state === "pending" ? "text.secondary" : "text.primary"}
          fontWeight={state === "active" ? 600 : 400}
        >
          {label}
        </Typography>
      </Stack>
      {error && (
        <Typography variant="caption" color="warning.main" sx={{ pl: 3.5, display: "block" }}>
          {error}
        </Typography>
      )}
      {children}
    </Box>
  );
}

export function StaffingStepper({ progress, done }: { progress: StaffingProgress; done: boolean }) {
  const matchLabel =
    progress.matchTotal != null
      ? `Matching (${progress.matchCompleted}/${progress.matchTotal})`
      : "Matching";
  return (
    <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2 }} data-testid="staffing-stepper">
      <Stack spacing={0.75}>
        <StaffingStepRow id="shortlist" label="Shortlisting" state={progress.shortlist} />
        <StaffingStepRow id="match" label={matchLabel} state={progress.match}>
          {progress.matchTicks.length > 0 && (
            <Stack spacing={0.25} sx={{ pl: 3.5, mt: 0.25 }}>
              {progress.matchTicks.map((t, i) => (
                <Stack
                  key={i}
                  direction="row"
                  spacing={0.5}
                  alignItems="center"
                  data-testid="staffing-match-tick"
                >
                  {t.failed && <WarningAmberIcon fontSize="small" color="warning" />}
                  <Typography variant="caption" color="text.secondary">
                    {t.name}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          )}
        </StaffingStepRow>
        <StaffingStepRow
          id="narrative"
          label="Composing recommendation"
          state={progress.narrative}
          error={progress.narrativeError}
        />
        <StaffingStepRow id="done" label="Done" state={done ? "done" : "pending"} />
      </Stack>
    </Paper>
  );
}
