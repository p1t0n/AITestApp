import { useState } from "react";
import {
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import EditIcon from "@mui/icons-material/Edit";
import {
  apiErrorMessage,
  useApproveClaim,
  useClaimQueue,
  useDeleteUser,
  useRejectClaim,
  useUpdateUser,
  useUsers,
  type ClaimQueueItem,
  type UpdateUser,
  type UserStatus,
  type UserSummary,
} from "../api";
import { ErrorNotice } from "../components/ErrorNotice";
import PageHeader from "../components/PageHeader";
import ClaimQueue from "../components/ClaimQueue";

const capLabel = (v: number | null) => (v === null ? "default" : v.toLocaleString());

export default function UsersPage() {
  const { data: users, isLoading, isError, error } = useUsers();
  const claims = useClaimQueue();
  const approveClaim = useApproveClaim();
  const rejectClaim = useRejectClaim();
  const updateUser = useUpdateUser();
  const deleteUser = useDeleteUser();
  const [editing, setEditing] = useState<UserSummary | null>(null);

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

  const toggleStatus = (u: UserSummary) => {
    const next: UserStatus = u.status === "Active" ? "Deactivated" : "Active";
    updateUser.mutate({
      id: u.id,
      email: u.email,
      status: next,
      dailyTokenCap: u.dailyTokenCap,
      weeklyTokenCap: u.weeklyTokenCap,
      monthlyTokenCap: u.monthlyTokenCap,
    });
  };

  const remove = (u: UserSummary) => {
    if (window.confirm(`Delete ${u.email}? This removes the account and its passkeys.`)) {
      deleteUser.mutate(u.id);
    }
  };

  return (
    // Seven columns of caps and counts — a table, so the same wide cap as the roster.
    <PageHeader title="Users" width="wide">
      {/* Stays in the body rather than becoming the header's subtitle: it is two lines of policy,
          and a sticky strip is not where a paragraph belongs. */}
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Anyone signed in can manage any account (flat roles). Token caps blank as "default" inherit
        the system-wide limit.
      </Typography>

      <ErrorNotice
        message={
          claims.isError || approveClaim.isError || rejectClaim.isError
            ? apiErrorMessage(claims.error ?? approveClaim.error ?? rejectClaim.error)
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

      <Typography variant="h6" component="h2" sx={{ mb: 2 }}>
        Accounts
      </Typography>

      <ErrorNotice message={isError ? apiErrorMessage(error) : null} />
      <ErrorNotice
        message={
          updateUser.isError || deleteUser.isError
            ? apiErrorMessage(updateUser.error ?? deleteUser.error)
            : null
        }
        sx={{ mb: 2 }}
      />

      <Paper>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Email</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Passkeys</TableCell>
              <TableCell align="right">Daily</TableCell>
              <TableCell align="right">Weekly</TableCell>
              <TableCell align="right">Monthly</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && (
              <TableRow>
                <TableCell colSpan={7}>Loading…</TableCell>
              </TableRow>
            )}
            {users?.map((u) => (
              <TableRow key={u.id} hover>
                <TableCell>{u.email}</TableCell>
                <TableCell>
                  <Chip
                    label={u.status}
                    color={u.status === "Active" ? "success" : "default"}
                    variant={u.status === "Active" ? "filled" : "outlined"}
                  />
                </TableCell>
                <TableCell align="right">{u.passkeyCount}</TableCell>
                <TableCell align="right">{capLabel(u.dailyTokenCap)}</TableCell>
                <TableCell align="right">{capLabel(u.weeklyTokenCap)}</TableCell>
                <TableCell align="right">{capLabel(u.monthlyTokenCap)}</TableCell>
                <TableCell align="right">
                  <Button onClick={() => toggleStatus(u)} disabled={updateUser.isPending}>
                    {u.status === "Active" ? "Deactivate" : "Activate"}
                  </Button>
                  <Tooltip title="Edit">
                    <IconButton onClick={() => setEditing(u)}>
                      <EditIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Delete">
                    <IconButton color="error" onClick={() => remove(u)}>
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </TableCell>
              </TableRow>
            ))}
            {users?.length === 0 && (
              <TableRow>
                <TableCell colSpan={7}>No users yet.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Paper>

      {editing && (
        <EditUserDialog
          user={editing}
          onClose={() => setEditing(null)}
          onSave={(dto) =>
            updateUser.mutate(
              { id: editing.id, ...dto },
              { onSuccess: () => setEditing(null) },
            )
          }
          saving={updateUser.isPending}
        />
      )}
    </PageHeader>
  );
}

function EditUserDialog({
  user,
  onClose,
  onSave,
  saving,
}: {
  user: UserSummary;
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

  const handleSave = () => {
    onSave({
      email: email.trim(),
      status,
      dailyTokenCap: toCap(daily),
      weeklyTokenCap: toCap(weekly),
      monthlyTokenCap: toCap(monthly),
    });
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Edit user</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} fullWidth />
          <TextField
            label="Status"
            select
            value={status}
            onChange={(e) => setStatus(e.target.value as UserStatus)}
            fullWidth
          >
            <MenuItem value="Active">Active</MenuItem>
            <MenuItem value="Deactivated">Deactivated</MenuItem>
          </TextField>
          <Typography variant="body2" color="text.secondary">
            Token caps — leave blank to inherit the system default.
          </Typography>
          <Stack direction="row" spacing={2}>
            <TextField label="Daily" type="number" value={daily} onChange={(e) => setDaily(e.target.value)} fullWidth />
            <TextField label="Weekly" type="number" value={weekly} onChange={(e) => setWeekly(e.target.value)} fullWidth />
            <TextField label="Monthly" type="number" value={monthly} onChange={(e) => setMonthly(e.target.value)} fullWidth />
          </Stack>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={handleSave} disabled={saving}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
