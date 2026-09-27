import { useMemo, useState } from "react";
import {
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  MenuItem,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import EditIcon from "@mui/icons-material/Edit";
import {
  apiErrorMessage,
  LAST_ADMINISTRATOR_REFUSAL,
  SELF_ROLE_REFUSAL,
  useApproveClaim,
  useChangeUserRole,
  useClaimQueue,
  useContestQueue,
  useDeleteUser,
  useRejectClaim,
  useReviewContest,
  useUpdateUser,
  useUsers,
  type ClaimQueueItem,
  type ContestOutcome,
  type ContestQueueItem,
  type UpdateUser,
  type UserStatus,
  type UserSummary,
} from "../api";
import type { SessionRole } from "../auth/roles";
import { useSessionUserId } from "../auth/useAuth";
import { ErrorNotice } from "../components/ErrorNotice";
import PageHeader from "../components/PageHeader";
import ClaimQueue from "../components/ClaimQueue";
import ContestQueue from "../components/ContestQueue";
import DictionaryTable, { type DictionaryColumn } from "../components/DictionaryTable";
import EditDialog from "../components/EditDialog";

const capLabel = (v: number | null) => (v === null ? "default" : v.toLocaleString());

/**
 * A cap's sort key. `null` means "inherit the system default", which is not a number and has no
 * honest place on the scale — it sorts below every explicit cap rather than being dropped, so the
 * column still accounts for every row.
 */
const capOrder = (v: number | null) => v ?? -1;

/**
 * What a demotion costs, in one sentence, shown before it happens (P1T-239).
 *
 * Exported so the test asserts the wording rather than a paraphrase of it: this is the only warning
 * anybody gets. There is no email on this service, so the person being demoted finds out by being
 * signed out — the four surfaces and the session are named here because that is the whole content
 * of the change.
 */
export const DEMOTION_CONSEQUENCE =
  "They lose the roster, the skill catalog, the agent surfaces and this page, and their session "
  + "ends immediately — they are signed out, and sign back in to their own CV.";

/** The roles on offer, in the order the column reads them. */
const ROLES: readonly SessionRole[] = ["Administrator", "User"];

const STATUSES: readonly UserStatus[] = ["Active", "Deactivated"];

export default function UsersPage() {
  const { data: users, isLoading, isError, error } = useUsers();
  const claims = useClaimQueue();
  const contests = useContestQueue();
  const reviewContest = useReviewContest();
  const approveClaim = useApproveClaim();
  const rejectClaim = useRejectClaim();
  const updateUser = useUpdateUser();
  const deleteUser = useDeleteUser();
  const changeRole = useChangeUserRole();
  // The *id* being edited, not the row: a role change refetches the list, and a captured row would
  // leave the popup's own selector showing the value it was opened with.
  const [editingId, setEditingId] = useState<string | null>(null);
  // The demotion waiting on an answer, or null. The row rather than the id, because the question
  // names the account.
  const [demoting, setDemoting] = useState<UserSummary | null>(null);
  const [deleting, setDeleting] = useState<UserSummary | null>(null);
  const signedInUserId = useSessionUserId();

  const editing = users?.find((u) => u.id === editingId) ?? null;

  // Both refusals the server enforces, computed from what this page already holds — the loaded list
  // and the session. Shown as text beside the control rather than hidden behind a disabled input:
  // a blocked control with no reason on it reads as broken, not as protected.
  const administrators = users?.filter((u) => u.role === "Administrator").length ?? 0;
  const refusalFor = (u: UserSummary): string | null => {
    if (u.id === signedInUserId) return SELF_ROLE_REFUSAL;
    if (u.role === "Administrator" && administrators <= 1) return LAST_ADMINISTRATOR_REFUSAL;
    return null;
  };

  // Promotion applies on the spot; demotion asks first. Not symmetry for its own sake — a demotion
  // ends somebody's session and takes four surfaces away, and an undo would have to be done by
  // somebody else.
  const chooseRole = (u: UserSummary, next: SessionRole) => {
    if (next === u.role) return;
    if (next === "User") setDemoting(u);
    else changeRole.mutate({ id: u.id, role: next });
  };

  const approve = (claim: ClaimQueueItem) => {
    if (
      window.confirm(
        `Bind ${claim.expertEmail ?? "this record"} to ${claim.claimantEmail}?\n\n` +
          "The only evidence is a matching email address, which is never verified. " +
          "They will be able to read and edit that record, and it becomes scannable for Jobs.",
      )
    ) {
      approveClaim.mutate(claim.id);
    }
  };

  /**
   * The eight columns, and the rule that shapes them: not one of them writes anything. Status used
   * to carry an Activate/Deactivate button right in the row — one click from ending somebody's
   * access — and it is now a field in the edit popup like every other editable thing.
   */
  const columns: DictionaryColumn<UserSummary>[] = useMemo(
    () => [
      { key: "email", label: "Email", sortValue: (u) => u.email.toLowerCase(), render: (u) => u.email },
      { key: "role", label: "Role", sortValue: (u) => u.role, render: (u) => u.role },
      {
        key: "status",
        label: "Status",
        sortValue: (u) => u.status,
        render: (u) => (
          <Chip
            label={u.status}
            color={u.status === "Active" ? "success" : "default"}
            variant={u.status === "Active" ? "filled" : "outlined"}
          />
        ),
      },
      {
        key: "passkeys",
        label: "Passkeys",
        align: "right",
        sortValue: (u) => u.passkeyCount,
        render: (u) => u.passkeyCount,
      },
      {
        key: "daily",
        label: "Daily",
        align: "right",
        sortValue: (u) => capOrder(u.dailyTokenCap),
        render: (u) => capLabel(u.dailyTokenCap),
      },
      {
        key: "weekly",
        label: "Weekly",
        align: "right",
        sortValue: (u) => capOrder(u.weeklyTokenCap),
        render: (u) => capLabel(u.weeklyTokenCap),
      },
      {
        key: "monthly",
        label: "Monthly",
        align: "right",
        sortValue: (u) => capOrder(u.monthlyTokenCap),
        render: (u) => capLabel(u.monthlyTokenCap),
      },
      {
        key: "actions",
        label: "Actions",
        align: "right",
        render: (u) => (
          <>
            <Tooltip title="Edit">
              <IconButton onClick={() => setEditingId(u.id)}>
                <EditIcon fontSize="small" />
              </IconButton>
            </Tooltip>
            <Tooltip title="Delete">
              <IconButton color="error" onClick={() => setDeleting(u)}>
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </>
        ),
      },
    ],
    [],
  );

  return (
    // Eight columns of roles, caps and counts — a table, so the same wide cap as the roster.
    <PageHeader title="Users" width="wide">
      {/* Stays in the body rather than becoming the header's subtitle: it is two lines of policy,
          and a sticky strip is not where a paragraph belongs. */}
      <Typography
        variant="body2"
        sx={{ color: "text.secondary", mb: 2 }}>
        An Administrator manages every account, their own included — except for its role, which
        somebody else has to change. Changing a role signs that account out immediately. Token caps
        blank as "default" inherit the system-wide limit.
      </Typography>

      <ErrorNotice
        message={
          claims.isError || approveClaim.isError || rejectClaim.isError
          || contests.isError || reviewContest.isError
            ? apiErrorMessage(
                claims.error ?? approveClaim.error ?? rejectClaim.error
                ?? contests.error ?? reviewContest.error)
            : null
        }
        sx={{ mb: 2 }}
      />
      <ClaimQueue
        claims={claims.data}
        loading={claims.isLoading}
        onApprove={approve}
        onReject={(claim) => rejectClaim.mutate(claim.id)}
        busy={approveClaim.isPending || rejectClaim.isPending}
      />

      {/* The second thing only a person can decide, on the same page as the first (P1T-189). Two
          lists rather than one: a claim and a contested score need different columns and different
          verbs, and merging them would make both harder to read. */}
      <ContestQueue
        contests={contests.data}
        loading={contests.isLoading}
        onReview={(item: ContestQueueItem, outcome: ContestOutcome, response: string) =>
          reviewContest.mutate({
            scoringCandidateId: item.scoringCandidateId,
            outcome,
            response: response.trim() === "" ? undefined : response,
          })
        }
        busy={reviewContest.isPending}
      />

      <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
        Accounts
      </Typography>

      <ErrorNotice message={isError ? apiErrorMessage(error) : null} />
      <ErrorNotice
        message={
          updateUser.isError || deleteUser.isError || changeRole.isError
            ? apiErrorMessage(updateUser.error ?? deleteUser.error ?? changeRole.error)
            : null
        }
        sx={{ mb: 2 }}
      />

      <DictionaryTable
        label="Accounts"
        rows={users}
        columns={columns}
        rowKey={(u) => u.id}
        search={{ label: "Search email", of: (u) => u.email }}
        filters={[
          { key: "role", label: "Role", options: ROLES, valueOf: (u) => u.role },
          { key: "status", label: "Status", options: STATUSES, valueOf: (u) => u.status },
        ]}
        initialSort={{ key: "email", dir: "asc" }}
        loading={isLoading}
        empty="No users yet."
        emptyFiltered="No accounts match."
      />

      {demoting && (
        <ConfirmDemotionDialog
          user={demoting}
          busy={changeRole.isPending}
          onClose={() => setDemoting(null)}
          onConfirm={() =>
            changeRole.mutate(
              { id: demoting.id, role: "User" },
              { onSuccess: () => setDemoting(null) },
            )
          }
        />
      )}

      {deleting && (
        <ConfirmDeleteDialog
          user={deleting}
          busy={deleteUser.isPending}
          onClose={() => setDeleting(null)}
          onConfirm={() =>
            deleteUser.mutate(deleting.id, { onSuccess: () => setDeleting(null) })
          }
        />
      )}

      {editing && (
        <EditUserDialog
          key={editing.id}
          user={editing}
          refusal={refusalFor(editing)}
          roleBusy={changeRole.isPending}
          onChooseRole={(next) => chooseRole(editing, next)}
          onClose={() => setEditingId(null)}
          onSave={(dto) =>
            updateUser.mutate(
              { id: editing.id, ...dto },
              { onSuccess: () => setEditingId(null) },
            )
          }
          saving={updateUser.isPending}
        />
      )}
    </PageHeader>
  );
}

/**
 * One account's whole editable surface, and the only place any of it can be changed.
 *
 * The role sits here with the rest of it but does not behave like the rest of it: it has its own
 * endpoint (`PUT /users/{id}/role`), so choosing one writes immediately instead of waiting for
 * Save, and it is deliberately outside the dirty check. A role that Save could forget to send would
 * be worse than one that applies on the spot.
 */
function EditUserDialog({
  user,
  refusal,
  roleBusy,
  onChooseRole,
  onClose,
  onSave,
  saving,
}: {
  user: UserSummary;
  /** Why this account's role cannot change, in the server's words — or null when it can. */
  refusal: string | null;
  roleBusy: boolean;
  onChooseRole: (role: SessionRole) => void;
  onClose: () => void;
  onSave: (dto: UpdateUser) => void;
  saving: boolean;
}) {
  const [email, setEmail] = useState(user.email);
  const [status, setStatus] = useState<UserStatus>(user.status);
  const [daily, setDaily] = useState(user.dailyTokenCap?.toString() ?? "");
  const [weekly, setWeekly] = useState(user.weeklyTokenCap?.toString() ?? "");
  const [monthly, setMonthly] = useState(user.monthlyTokenCap?.toString() ?? "");

  const toCap = (s: string): number | null => {
    const t = s.trim();
    return t === "" ? null : Number(t);
  };
  const capText = (v: number | null) => v?.toString() ?? "";

  const dirty =
    email !== user.email
    || status !== user.status
    || daily !== capText(user.dailyTokenCap)
    || weekly !== capText(user.weeklyTokenCap)
    || monthly !== capText(user.monthlyTokenCap);

  return (
    <EditDialog
      title={`Edit ${user.email}`}
      dirty={dirty}
      saving={saving}
      onClose={onClose}
      onSave={() =>
        onSave({
          email: email.trim(),
          status,
          dailyTokenCap: toCap(daily),
          weeklyTokenCap: toCap(weekly),
          monthlyTokenCap: toCap(monthly),
        })
      }
    >
      <TextField label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} fullWidth />
      <Stack spacing={0.5}>
        <TextField
          label="Role"
          select
          value={user.role}
          disabled={refusal !== null || roleBusy}
          onChange={(e) => onChooseRole(e.target.value as SessionRole)}
          slotProps={{ htmlInput: { "aria-label": `Role for ${user.email}` } }}
          data-testid="users-role-select"
          fullWidth
        >
          {ROLES.map((role) => (
            <MenuItem key={role} value={role}>
              {role}
            </MenuItem>
          ))}
        </TextField>
        <Typography variant="caption" sx={{ color: "text.secondary" }}>
          {refusal ?? "Applies immediately, and signs that account out."}
        </Typography>
      </Stack>
      <TextField
        label="Status"
        select
        value={status}
        onChange={(e) => setStatus(e.target.value as UserStatus)}
        fullWidth
      >
        {STATUSES.map((s) => (
          <MenuItem key={s} value={s}>
            {s}
          </MenuItem>
        ))}
      </TextField>
      <Typography variant="body2" sx={{ color: "text.secondary" }}>
        Token caps — leave blank to inherit the system default.
      </Typography>
      <Stack direction="row" spacing={2}>
        <TextField label="Daily" type="number" value={daily} onChange={(e) => setDaily(e.target.value)} fullWidth />
        <TextField label="Weekly" type="number" value={weekly} onChange={(e) => setWeekly(e.target.value)} fullWidth />
        <TextField label="Monthly" type="number" value={monthly} onChange={(e) => setMonthly(e.target.value)} fullWidth />
      </Stack>
    </EditDialog>
  );
}

/**
 * The question asked before a demotion, and only before a demotion — a promotion takes nothing
 * away, so stopping to confirm one would train people to click through this dialog.
 */
function ConfirmDemotionDialog({
  user,
  busy,
  onClose,
  onConfirm,
}: {
  user: UserSummary;
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
}) {
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" data-testid="users-role-confirm">
      <DialogTitle>Demote {user.email} to User?</DialogTitle>
      <DialogContent>
        <DialogContentText>{DEMOTION_CONSEQUENCE}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color="error" variant="contained" onClick={onConfirm} disabled={busy}>
          Demote to User
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/**
 * Deleting an account, asked in a dialog rather than `window.confirm`.
 *
 * Not a cosmetic swap: a native confirm is unstyleable, unreadable to the e2e suite, and — being
 * the browser's own chrome — looks identical to every other page's. The convention is that a write
 * happens in a popup this app drew.
 */
function ConfirmDeleteDialog({
  user,
  busy,
  onClose,
  onConfirm,
}: {
  user: UserSummary;
  busy: boolean;
  onClose: () => void;
  onConfirm: () => void;
}) {
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Delete {user.email}?</DialogTitle>
      <DialogContent>
        <DialogContentText>
          This removes the account and its passkeys. They cannot sign in again, and it cannot be
          undone.
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color="error" variant="contained" onClick={onConfirm} disabled={busy}>
          Delete account
        </Button>
      </DialogActions>
    </Dialog>
  );
}
