import { useState } from "react";
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Stack,
  TextField,
} from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import type { SaveExpert } from "../types";
import { apiErrorMessage } from "../api";
import EditDialog from "../components/EditDialog";
import { ErrorNotice } from "../components/ErrorNotice";
import { SPECIAL_CATEGORY_GUIDANCE } from "./cvGuidance";

interface Props {
  /**
   * Locks the email field with an explanation (P1T-184, P1T-190). Not a control hidden by role —
   * the field is there, visibly frozen, and the server refuses a change from anybody but a Service
   * Manager regardless. The address is login identifier, claim key and CV contact at once with no
   * verification behind any of them, so its owner is exactly who must not be able to move it.
   */
  emailLocked?: boolean;
  open: boolean;
  title: string;
  initial?: Partial<SaveExpert>;
  onClose: () => void;
  onSave: (dto: SaveExpert) => Promise<unknown>;
  /**
   * Deleting the record this form is editing (EXP-50). Supplied only where deletion belongs — the
   * roster's Edit… — and never on the create form, which has nothing to delete yet. A confirmation
   * is asked here before it is ever called.
   */
  onDelete?: () => Promise<unknown>;
  /** What the confirmation names, so nobody deletes the wrong person off a generic question. */
  deleteSubject?: string;
}

const empty: SaveExpert = {
  firstName: "",
  lastName: "",
  title: "",
  email: "",
  phone: null,
  location: null,
  summary: null,
  photoUrl: null,
};

/**
 * The expert form, in the popup every expert write happens in (EXP-50).
 *
 * `open` unmounts the form rather than hiding it, which is the whole reason the dirty check below
 * can be trusted: a form kept mounted across two openings would still be holding the last one's
 * typing, and would greet the next person with "Unsaved changes" about work that was never theirs.
 */
export default function ExpertFormDialog({ open, ...rest }: Props) {
  if (!open) return null;
  return <ExpertForm {...rest} />;
}

function ExpertForm({
  title,
  initial,
  onClose,
  onSave,
  onDelete,
  deleteSubject,
  emailLocked = false,
}: Omit<Props, "open">) {
  // The record as it was handed over, frozen for the life of this opening — it is what "dirty" is
  // measured against. State with a lazy initialiser rather than a ref, because it *is* read during
  // render, which is the one thing a ref is not for.
  const [pristine] = useState<SaveExpert>(() => ({ ...empty, ...initial }));
  const [form, setForm] = useState<SaveExpert>(pristine);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const field =
    (key: keyof SaveExpert) =>
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setForm((f) => ({ ...f, [key]: e.target.value }));

  // A blank optional field arrives as `null` from the server and as `""` from the input, and the
  // two mean the same thing. Comparing them raw would call an untouched form dirty.
  const dirty = (Object.keys(empty) as (keyof SaveExpert)[]).some(
    (key) => (form[key] ?? "") !== (pristine[key] ?? ""),
  );

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      await onSave(form);
      onClose();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete() {
    setDeleting(true);
    setError(null);
    try {
      await onDelete!();
      setConfirmingDelete(false);
      onClose();
    } catch (err) {
      setConfirmingDelete(false);
      setError(apiErrorMessage(err));
    } finally {
      setDeleting(false);
    }
  }

  return (
    <>
      <EditDialog
        title={title}
        dirty={dirty}
        saving={saving}
        onClose={onClose}
        onSave={handleSave}
        recordAction={
          onDelete && (
            <Button color="error" startIcon={<DeleteIcon />} onClick={() => setConfirmingDelete(true)}>
              Delete
            </Button>
          )
        }
      >
        <ErrorNotice message={error} />
        <Stack direction="row" spacing={2}>
          <TextField label="First name" value={form.firstName} onChange={field("firstName")} fullWidth />
          <TextField label="Last name" value={form.lastName} onChange={field("lastName")} fullWidth />
        </Stack>
        <TextField label="Title" value={form.title} onChange={field("title")} fullWidth />
        <TextField
          label="Email"
          value={form.email}
          onChange={field("email")}
          fullWidth
          disabled={emailLocked}
          helperText={
            emailLocked
              ? "Your email address is set when you register and can only be changed by an Administrator. It identifies your account and links you to this record."
              : undefined
          }
        />
        <Stack direction="row" spacing={2}>
          <TextField label="Phone" value={form.phone ?? ""} onChange={field("phone")} fullWidth />
          <TextField label="Location" value={form.location ?? ""} onChange={field("location")} fullWidth />
        </Stack>
        <TextField
          label="Summary"
          value={form.summary ?? ""}
          onChange={field("summary")}
          fullWidth
          multiline
          minRows={3}
          // Art. 9 minimisation, said where the free text is actually typed (P1T-183). This is a
          // mitigation and not a solution — nothing stops somebody writing it anyway — but asking
          // is the only honest control available, since classifying the text on save would create
          // the very special-category inference it aims to avoid.
          helperText={SPECIAL_CATEGORY_GUIDANCE}
        />
        <TextField label="Photo URL" value={form.photoUrl ?? ""} onChange={field("photoUrl")} fullWidth />
      </EditDialog>

      {confirmingDelete && (
        <ConfirmDeleteDialog
          subject={deleteSubject ?? `${form.firstName} ${form.lastName}`.trim()}
          busy={deleting}
          onClose={() => setConfirmingDelete(false)}
          onConfirm={handleDelete}
        />
      )}
    </>
  );
}

/**
 * The question asked before a person is removed, in a dialog this app drew.
 *
 * The roster used to ask it with `window.confirm` from an icon on the row (EXP-50) — unstyleable,
 * unreadable to the e2e suite, and one mis-aimed click away at all times. It sits on top of the
 * edit dialog rather than replacing it, the same way `UsersPage` asks about a demotion, so backing
 * out returns to the form with the typing still in it.
 */
function ConfirmDeleteDialog({
  subject,
  busy,
  onClose,
  onConfirm,
}: {
  subject: string;
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
}) {
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Delete {subject}?</DialogTitle>
      <DialogContent>
        <DialogContentText>
          This removes the record, its CV and everything on it. It cannot be undone.
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color="error" variant="contained" onClick={onConfirm} disabled={busy}>
          Delete expert
        </Button>
      </DialogActions>
    </Dialog>
  );
}
