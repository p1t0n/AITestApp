import { useState, type ReactNode } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
} from "@mui/material";

/**
 * The popup every dictionary edit happens in (EXP-46).
 *
 * `DictionaryTable` keeps writes out of the row so a stray click cannot change data; this keeps the
 * same click from throwing typed work away. A clean dialog dismisses as cheaply as any other — Esc,
 * the backdrop, Cancel. A dirty one refuses all three and asks the question instead.
 *
 * The question is an `Alert` inside the dialog rather than a second `Dialog` on top of it: a modal
 * over a modal steals focus from the fields it is asking about, and the answer "Keep editing" has
 * to put it straight back.
 */

/** The chip that marks a dialog as holding unsaved work. */
export const UNSAVED_CHANGES = "Unsaved changes";

/** The question a dirty dialog asks before it will close. */
export const DISCARD_QUESTION = "Discard your changes?";

export interface EditDialogProps {
  title: ReactNode;
  /** Whether the form holds work that closing would lose. */
  dirty: boolean;
  /** A save already in flight — the button is spent until it lands. */
  saving?: boolean;
  /**
   * An action on the *record* rather than on this edit — Delete, in practice (EXP-50). It sits at
   * the far end of the action row, a row's width from Save, and putting a confirmation behind it is
   * the caller's job: this dialog only guards work that would be lost, not work that is destroyed.
   */
  recordAction?: ReactNode;
  /**
   * Whether the form is complete enough to send. Defaults to true, so a dialog whose every field is
   * optional says nothing. A form with a required field it cannot default — a new skill's category —
   * sets it false, because "dirty" only knows something was typed, not that it was enough.
   */
  canSave?: boolean;
  onClose: () => void;
  onSave: () => void;
  children: ReactNode;
}

export default function EditDialog({
  title,
  dirty,
  saving,
  canSave = true,
  recordAction,
  onClose,
  onSave,
  children,
}: EditDialogProps) {
  const [asking, setAsking] = useState(false);

  // Every dismissal route lands here, including MUI's own `onClose` — which is the single reason
  // both Esc and the backdrop are covered, and the reason a new route cannot be added without it.
  const tryClose = () => (dirty ? setAsking(true) : onClose());

  return (
    <Dialog open onClose={tryClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {title}
        {dirty && <Chip size="small" color="warning" label={UNSAVED_CHANGES} sx={{ ml: 1 }} />}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {children}
          {asking && (
            <Alert
              severity="warning"
              action={
                <>
                  <Button color="inherit" size="small" onClick={() => setAsking(false)}>
                    Keep editing
                  </Button>
                  <Button color="inherit" size="small" onClick={onClose}>
                    Discard
                  </Button>
                </>
              }
            >
              {DISCARD_QUESTION}
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {recordAction && (
          <>
            {recordAction}
            <Box sx={{ flex: 1 }} />
          </>
        )}
        <Button onClick={tryClose}>Cancel</Button>
        <Button variant="contained" onClick={onSave} disabled={!dirty || saving || !canSave}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
