import {
  Alert,
  Button,
  Chip,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from "@mui/material";
import type { ClaimQueueItem } from "../api";

/**
 * The claim queue (P1T-184). Lives on the Users page rather than in a place of its own because it
 * is an account-shaped decision, and it shares that page with the Art. 22 contest queue.
 *
 * The warning above the table is a design requirement, not decoration. The approver has no
 * verification signal at all — email is never verified here and no mail is ever sent — so the only
 * evidence in front of them is two strings being equal, which proves nothing. An approval screen
 * that looks authoritative invites rubber-stamping, so this one says out loud what it does not know.
 */
export const CLAIM_EVIDENCE_WARNING =
  "A matching email address proves nothing. It is never verified and this service sends no mail, " +
  "so approving binds this person to that record on your judgement alone. If you cannot place them, " +
  "issue a claim code from the expert's page and hand it over in person instead.";

export default function ClaimQueue({
  claims,
  loading,
  onApprove,
  onReject,
  busy,
}: {
  claims: ClaimQueueItem[] | undefined;
  loading: boolean;
  onApprove: (claim: ClaimQueueItem) => void;
  onReject: (claim: ClaimQueueItem) => void;
  busy: boolean;
}) {
  return (
    <Stack spacing={2} sx={{ mb: 4 }}>
      <Typography variant="h6" component="h2">
        Claim requests
      </Typography>

      <Alert severity="warning">{CLAIM_EVIDENCE_WARNING}</Alert>

      <Paper>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Claimant</TableCell>
              <TableCell>Record claimed</TableCell>
              <TableCell>Raised</TableCell>
              <TableCell align="right">Decision</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={4}>Loading…</TableCell>
              </TableRow>
            )}
            {claims?.map((claim) => (
              <TableRow key={claim.id} hover>
                <TableCell>{claim.claimantEmail}</TableCell>
                <TableCell>
                  {claim.expertId ? (
                    <Stack spacing={0.5}>
                      <span>{claim.expertName || claim.expertEmail}</span>
                      <Typography variant="caption" color="text.secondary">
                        {claim.expertEmail}
                      </Typography>
                    </Stack>
                  ) : (
                    <Stack spacing={0.5} direction="row" alignItems="center">
                      <Chip label="No record picked" color="warning" size="small" />
                      <Typography variant="caption" color="text.secondary">
                        {claim.matchCount === 1
                          ? "The one record with this address already belongs to another account, so nothing was claimed."
                          : `${claim.matchCount} records carry this address, so nothing was claimed.`}{" "}
                        Issue a claim code for the right record, then dismiss this.
                      </Typography>
                    </Stack>
                  )}
                </TableCell>
                <TableCell>{new Date(claim.createdAt).toLocaleDateString()}</TableCell>
                <TableCell align="right">
                  {claim.expertId && (
                    <Button onClick={() => onApprove(claim)} disabled={busy}>
                      Approve
                    </Button>
                  )}
                  <Button color="error" onClick={() => onReject(claim)} disabled={busy}>
                    {claim.expertId ? "Reject" : "Dismiss"}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {claims?.length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>Nothing waiting.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Paper>
    </Stack>
  );
}
