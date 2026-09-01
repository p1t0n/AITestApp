import {
  Alert,
  Button,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import { apiErrorMessage, useMyVisibility, useSetMyVisibility } from "../api";
import { ErrorNotice } from "./ErrorNotice";

/**
 * What pausing actually does, said in full before the button is pressed (P1T-185). Every clause is
 * load-bearing: people are choosing between this and deletion, and with no email there is no way to
 * reach somebody who deleted when they meant to pause.
 */
export const PAUSE_CONSEQUENCE =
  "You stop being offered for work: you will not appear in searches, matches or the scans that " +
  "rank people against jobs. Nothing is deleted — your record and everything in it stays exactly " +
  "as it is, Service Managers can still see it, and you can come back whenever you like.";

/**
 * The Expert's pause control. Deliberately a separate control from deletion and deliberately not
 * next to it (P1T-171): conflating the two is how somebody deletes when they meant to pause, and
 * this service can never mail them a way back.
 *
 * <p>Rendered only for a person who owns a record — an account with a claim still waiting owns
 * nothing yet, and there is nothing to pause.</p>
 */
export default function BenchPauseControl() {
  const visibility = useMyVisibility();
  const setVisibility = useSetMyVisibility();

  // No record of their own (404) — a pending claim, or a brand-new account. Nothing to show.
  if (visibility.isError || !visibility.data) {
    return null;
  }

  const paused = visibility.data.hidden;

  return (
    <Paper variant="outlined" sx={{ p: 3, mt: 3 }}>
      <Stack spacing={2}>
        <Typography variant="h6" component="h2">
          {paused ? "You are paused" : "Being offered for work"}
        </Typography>

        <ErrorNotice
          message={setVisibility.isError ? apiErrorMessage(setVisibility.error) : null}
        />

        {paused ? (
          <Alert severity="info">
            You paused yourself
            {visibility.data.hiddenSince
              ? ` on ${new Date(visibility.data.hiddenSince).toLocaleDateString()}`
              : ""}
            . You are not being offered for work, and nothing has been deleted.
          </Alert>
        ) : (
          <Typography variant="body2" color="text.secondary">
            {PAUSE_CONSEQUENCE}
          </Typography>
        )}

        <Stack direction="row">
          <Button
            variant={paused ? "contained" : "outlined"}
            disabled={setVisibility.isPending}
            onClick={() => setVisibility.mutate(!paused)}
          >
            {paused ? "Start being offered for work again" : "Pause — stop offering me for work"}
          </Button>
        </Stack>
      </Stack>
    </Paper>
  );
}
