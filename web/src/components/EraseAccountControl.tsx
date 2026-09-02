import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { apiErrorMessage, useEraseMyAccount } from "../api";
import { clearSession } from "../auth/session";
import { ErrorNotice } from "./ErrorNotice";

/**
 * What deleting actually does, in full, before the control word is typed (P1T-186). The last clause
 * is the one that matters most and is the easiest to leave out: this service sends no email, so
 * there is no undo link, no support address that can restore anything, and no way for anybody to
 * tell you it happened.
 */
export const ERASE_CONSEQUENCE =
  "Your account and your record are deleted immediately and permanently: your CV, your skills, " +
  "your history, your sign-in — all of it. Proposals a Service Manager already decided on keep " +
  "their decision, with your name and everything written about you removed. This cannot be " +
  "undone, and there is no email on this service to recover an account with.";

/**
 * Erasure, at the foot of the workspace and under its own rule (P1T-171, P1T-191). It is
 * deliberately a long way from the pause control and looks like a different kind of thing, because
 * conflating them is how somebody deletes when they meant to pause — and with no email there is no
 * reaching them afterwards.
 */
export default function EraseAccountControl() {
  const erase = useEraseMyAccount();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [controlWord, setControlWord] = useState("");

  const confirm = () =>
    erase.mutate(controlWord, {
      onSuccess: () => {
        // The session died with the account — both hosts refuse it from here on — so the only
        // honest next screen is the signed-out one.
        clearSession();
        navigate("/signin", { replace: true });
      },
    });

  return (
    <Stack spacing={2} sx={{ mt: 8 }}>
      <Divider />

      <Typography variant="h6" component="h2">
        Delete everything
      </Typography>

      <Typography variant="body2" color="text.secondary">
        {ERASE_CONSEQUENCE}
      </Typography>

      <Typography variant="body2" color="text.secondary">
        If you only want to stop being offered for work, pause instead — it is reversible and
        nothing is lost.
      </Typography>

      <Stack direction="row">
        <Button color="error" variant="outlined" onClick={() => setOpen(true)}>
          Delete my account and my record
        </Button>
      </Stack>

      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Delete everything?</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>{ERASE_CONSEQUENCE}</DialogContentText>

          <ErrorNotice message={erase.isError ? apiErrorMessage(erase.error) : null} />

          <TextField
            label="Your control word"
            type="password"
            value={controlWord}
            onChange={(event) => setControlWord(event.target.value)}
            helperText="The word you chose when you signed up. It is what proves this is you."
            fullWidth
            autoFocus
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Keep my account</Button>
          <Button
            color="error"
            variant="contained"
            disabled={controlWord.trim() === "" || erase.isPending}
            onClick={confirm}
          >
            Delete everything
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
