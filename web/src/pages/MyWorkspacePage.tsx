import { Paper, Stack, Typography } from "@mui/material";
import PageHeader from "../components/PageHeader";
import NoticeUpdateBanner from "../components/NoticeUpdateBanner";
import RedeemClaimCode from "../components/RedeemClaimCode";
import BenchPauseControl from "../components/BenchPauseControl";
import EraseAccountControl from "../components/EraseAccountControl";
import { useSessionEmail } from "../auth/useAuth";

/**
 * Where an Expert lands (P1T-181). Deliberately thin: this slice built the role split, and the
 * Expert workspace itself — My CV, the claim status, the transparency view — is P1T-190. What it
 * has to do today is exist and be reachable, because "redirect an Expert to their own landing page"
 * is meaningless without one, and bouncing them to `/signin` would tell a signed-in person that
 * they are signed out.
 */
export default function MyWorkspacePage() {
  const email = useSessionEmail();

  return (
    <PageHeader title="My workspace" width="content">
      {/* Where a changed transparency notice reaches an Expert (P1T-183): this is where they land
          after signing in, and a sign-in is the only channel a service that sends no email has.
          It notifies — everything below it stays readable and editable regardless. */}
      <NoticeUpdateBanner />

      <Paper variant="outlined" sx={{ p: 3 }}>
        <Stack spacing={1.5}>
          <Typography variant="body1">
            {email ? `You are signed in as ${email}.` : "You are signed in."}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            This is your own space. Your CV, the status of your claim on it, and what the service
            holds about you will appear here — nothing on this page is shared with the other people
            whose CVs are in the service.
          </Typography>
        </Stack>
      </Paper>

      {/* The pause: shown only to somebody who actually owns a record (P1T-185). Kept well away
          from anything destructive — pause and delete are two different kinds of act, and with no
          email there is no way back for somebody who confused them. */}
      <BenchPauseControl />

      {/* The way out of owning nothing (P1T-184). Shown to everybody rather than only to people
          with a pending claim: a session that owns no row is deliberately indistinguishable from
          one whose claim is waiting, so this page cannot tell which it is looking at — and
          somebody who was handed a code needs the field either way. */}
      <RedeemClaimCode />

      {/* Last on the page, under its own rule, a long way from the pause (P1T-171, P1T-186). The
          distance is the mechanism: pause and delete are different kinds of act, and this service
          cannot email anybody who confused them. */}
      <EraseAccountControl />
    </PageHeader>
  );
}
