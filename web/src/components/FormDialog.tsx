import { useState, type ReactNode } from "react";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
} from "@mui/material";
import { apiErrorMessage } from "../api";
import { ErrorNotice } from "./ErrorNotice";

/**
 * The popup the five expert-child forms are written in — language, availability, qualification,
 * skill, experience (EXP-81).
 *
 * `EditDialog` is the *dictionary* popup and guards unsaved work: a dirty chip, a discard question
 * before it will close, Save spent until something has been typed. These forms ask none of that —
 * they are mounted only while editing, and `ExpertRecordSections` unmounts them on close — so they
 * get the plainer shell instead of an `EditDialog` bent into two shapes. What the five copies
 * genuinely shared was not the markup but `handleSave`, and that is `useSaveAndClose`.
 *
 * There is no `open` prop: all five call sites passed the literal `open`, which is a prop that can
 * only ever be true.
 */
interface FormDialogProps {
  title: ReactNode;
  /** The last failure, rendered above the fields. Null when nothing has gone wrong. */
  error: string | null;
  /** A save already in flight — Save is spent until it lands. */
  saving: boolean;
  /**
   * Whether the form is complete enough to send. Defaults to true, because the server is the only
   * validator. A form sets it false only where an empty field would never *reach* validation — an
   * unpicked catalog skill, a blank date that fails model binding first.
   */
  canSave?: boolean;
  /** `xs` for the short forms; `sm` where a row of two fields needs the room. */
  maxWidth?: "xs" | "sm";
  onClose: () => void;
  onSave: () => void;
  children: ReactNode;
}

export default function FormDialog({
  title,
  error,
  saving,
  canSave = true,
  maxWidth = "xs",
  onClose,
  onSave,
  children,
}: FormDialogProps) {
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth={maxWidth}>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <ErrorNotice message={error} />
          {children}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={onSave} disabled={saving || !canSave}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/**
 * Save, then close — and on a failure, neither.
 *
 * The five forms each kept their own `saving`/`error` pair and an identical `handleSave`. The one
 * decision in it is the `catch`: the dialog stays open with the input intact, because a server
 * validation message is only actionable next to the field that caused it. Clearing the previous
 * error on the way in matters for the same reason — a stale message beside a fixed field is worse
 * than none.
 *
 * The DTO is passed to `save` rather than held here: what a form sends is often not what it holds
 * (a qualification nulls the half its type does not use, an experience renumbers its bullets).
 */
export function useSaveAndClose<T>(
  onSave: (dto: T) => Promise<unknown>,
  onClose: () => void,
) {
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function save(dto: T) {
    setSaving(true);
    setError(null);
    try {
      await onSave(dto);
      onClose();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  return { save, saving, error };
}
