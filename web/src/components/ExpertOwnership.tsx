import { useState } from "react";
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  Paper,
  Stack,
  Typography,
} from "@mui/material";
import { apiErrorMessage, useExpertOwnership, useIssueClaimCode, useRevokeOwnership } from "../api";
import { ErrorNotice } from "./ErrorNotice";

/**
 * The chain revocation sets off, written out because the button does not look like it does this
 * (P1T-184). Unowned means legitimate interest, and legitimate interest carries no Art. 22(2)
 * route — so an unclaimed record is not scanned, and revoking quietly removes somebody from
 * consideration for work. An approver who is not told that will use this button for tidying up.
 */
export const REVOKE_CONSEQUENCE =
  "This record becomes unclaimed, returns to legitimate interest, and is no longer scanned for " +
  "Jobs — so this person stops being considered. They lose access to it; the record itself is kept " +
  "and stays visible to Service Managers.";

/**
 * Who a roster row belongs to, and the two ways that changes (P1T-184). Unclaimed is a legitimate,
 * permanent state — most of the bench is unclaimed — but it is a degraded one, so it is said out
 * loud on the row rather than left to be worked out from an empty field.
 */
export default function ExpertOwnership({ expertId }: { expertId: string }) {
  const ownership = useExpertOwnership(expertId);
  const issueCode = useIssueClaimCode();
  const revoke = useRevokeOwnership();
  const [confirmRevoke, setConfirmRevoke] = useState(false);
  const [code, setCode] = useState<string | null>(null);

  const ownerEmail = ownership.data?.ownerEmail ?? null;
  const claimed = ownership.data?.ownerUserId != null;

  return (
    <Paper sx={{ p: 3, mb: 3 }}>
      <Stack direction="row" justifyContent="space-between" alignItems="center">
        <Typography variant="h6" gutterBottom>
          Ownership
        </Typography>
        {ownership.isLoading ? null : claimed ? (
          <Button color="error" onClick={() => setConfirmRevoke(true)} disabled={revoke.isPending}>
            Revoke ownership
          </Button>
        ) : (
          <Button
            onClick={() =>
              issueCode.mutate(expertId, { onSuccess: (issued) => setCode(issued.code) })
            }
            disabled={issueCode.isPending}
          >
            Issue claim code
          </Button>
        )}
      </Stack>
      <Divider sx={{ mb: 2 }} />

      <ErrorNotice
        message={
          ownership.isError || issueCode.isError || revoke.isError
            ? apiErrorMessage(ownership.error ?? issueCode.error ?? revoke.error)
            : null
        }
        sx={{ mb: 2 }}
      />

      {ownership.isLoading ? (
        <Typography color="text.secondary">Loading…</Typography>
      ) : claimed ? (
        <Typography>
          Claimed by <b>{ownerEmail}</b>. They can read and edit this record, and it is scanned for
          Jobs.
        </Typography>
      ) : (
        <Typography color="text.secondary">
          Nobody has claimed this record. It is held on legitimate interest, which carries no route
          to automated decisions — so it is <b>not scanned for Jobs</b>. Hand this person a claim
          code to change that.
        </Typography>
      )}

      {code && (
        <Alert severity="success" sx={{ mt: 2 }} onClose={() => setCode(null)}>
          <Typography variant="body2" gutterBottom>
            Give this code to the person, in person or by phone — never by email, which is exactly
            what this mechanism replaces. It works once and <b>will not be shown again</b>.
          </Typography>
          <Typography variant="h6" component="p" sx={{ fontFamily: "monospace" }}>
            {code}
          </Typography>
        </Alert>
      )}

      <Dialog open={confirmRevoke} onClose={() => setConfirmRevoke(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Revoke {ownerEmail}&rsquo;s ownership?</DialogTitle>
        <DialogContent>
          <DialogContentText>{REVOKE_CONSEQUENCE}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmRevoke(false)}>Cancel</Button>
          <Button
            color="error"
            variant="contained"
            disabled={revoke.isPending}
            onClick={() =>
              revoke.mutate(expertId, { onSuccess: () => setConfirmRevoke(false) })
            }
          >
            Revoke ownership
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}
