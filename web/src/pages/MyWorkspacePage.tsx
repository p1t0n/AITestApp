import { Paper, Stack, Typography } from "@mui/material";
import PageHeader from "../components/PageHeader";
import NoticeUpdateBanner from "../components/NoticeUpdateBanner";
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
    </PageHeader>
  );
}
