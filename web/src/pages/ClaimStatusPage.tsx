import { Navigate } from "react-router-dom";
import { Alert, CircularProgress, Paper, Stack, Typography } from "@mui/material";
import { useMyVisibility } from "../api";
import PageHeader, { PageContainer } from "../components/PageHeader";
import RedeemClaimCode from "../components/RedeemClaimCode";

/**
 * Where somebody lands when they own no record (P1T-190).
 *
 * <p>They exist here because an empty CV editor would misrepresent what is happening: nothing of
 * theirs is held, and a form full of blank fields reads as "fill this in" rather than "you are
 * waiting on somebody". Two situations reach this page and it deliberately does not distinguish
 * them — a claim waiting on a Service Manager is indistinguishable from no claim at all, by design
 * (P1T-182), and telling them apart here would undo the property that stops this surface confirming
 * whose records exist.</p>
 */
export default function ClaimStatusPage() {
  const mine = useMyVisibility();

  if (mine.isLoading) {
    return (
      <PageContainer width="content">
        <CircularProgress />
      </PageContainer>
    );
  }

  // They do own one after all — a claim was approved, or a code was redeemed in another tab.
  if (mine.data) {
    return <Navigate to="/me/cv" replace />;
  }

  return (
    <PageHeader title="Your record" width="content">
      <Alert severity="info" sx={{ mb: 3 }}>
        There is no record here yet under your name.
      </Alert>

      <Paper variant="outlined" sx={{ p: 3 }}>
        <Stack spacing={1.5}>
          <Typography variant="body1">What that means, and what happens next.</Typography>

          <Typography variant="body2" color="text.secondary">
            If a Service Manager already had a record for you when you signed up, they have to
            confirm it is yours before you can see it. That is deliberate: an email address is not
            proof of anything here, and handing somebody a CV on the strength of a matching address
            is exactly the mistake this step exists to prevent. Nothing is shown to you until a
            person has checked.
          </Typography>

          <Typography variant="body2" color="text.secondary">
            If there was no record for you, one will have been created and this page will be
            replaced by your CV. Try reloading.
          </Typography>

          <Typography variant="body2" color="text.secondary">
            This service never sends email, so nobody can write to tell you when it changes — sign
            in again and look.
          </Typography>
        </Stack>
      </Paper>

      {/* The way out that does not depend on waiting: a code handed over in person is the one piece
          of proof this service can offer that is stronger than a matching address (P1T-184). */}
      <RedeemClaimCode />
    </PageHeader>
  );
}
