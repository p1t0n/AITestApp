import type { ReactNode } from "react";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
} from "@mui/material";

/**
 * The question asked before something destructive, in a dialog this app drew (EXP-81).
 *
 * Four copies of it existed — the demotion and the account delete in `UsersPage`, the expert delete
 * in `ExpertFormDialog`, and one written inline in `ExpertOwnership` — differing only in their
 * strings. Not a cosmetic swap for `window.confirm`: a native confirm is unstyleable, unreadable to
 * the e2e suite, and, being the browser's own chrome, looks identical to every other page's. The
 * convention is that a write happens in a popup this app drew.
 *
 * It sits *on top of* whatever opened it rather than replacing it, so backing out of the question
 * returns to the form with the typing still in it. Mount it only while the question is open — there
 * is no `open` prop, because a confirmation that is not being asked has nothing to render.
 *
 * The caller owns closing: `onConfirm` fires the write, and the mutation's own `onSuccess` is what
 * dismisses the dialog, so a failed write leaves the question on screen rather than silently
 * dropping it.
 */
interface ConfirmDialogProps {
  /** The question, as a question. */
  title: ReactNode;
  /** What confirming will actually do — the part a user is agreeing to. */
  body: ReactNode;
  /** The destructive verb, named rather than "OK": "Delete account", "Demote to User". */
  confirmLabel: string;
  /** The write is in flight — the button is spent until it lands. */
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
  /**
   * Passed through rather than owned. `users-role-confirm` is frozen DOM (`frozenHooks.test.ts`)
   * and belongs to the demotion question alone, so it is spelled at the call site, where the freeze
   * can still see the literal.
   */
  "data-testid"?: string;
}

export default function ConfirmDialog({
  title,
  body,
  confirmLabel,
  busy,
  onClose,
  onConfirm,
  "data-testid": testId,
}: ConfirmDialogProps) {
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" data-testid={testId}>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <DialogContentText>{body}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color="error" variant="contained" onClick={onConfirm} disabled={busy}>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
