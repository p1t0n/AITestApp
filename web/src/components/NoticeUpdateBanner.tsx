import { Alert, AlertTitle, Button, Collapse, Stack } from "@mui/material";
import { useState } from "react";
import { apiErrorMessage, useAcknowledgeNotice, useNoticeStatus } from "../api";
import { ErrorNotice } from "./ErrorNotice";
import { TransparencyNoticeText } from "./TransparencyNoticeText";

/**
 * Tells a signed-in Expert that the transparency notice has changed (P1T-183).
 *
 * This is the only channel there is: the service never sends email, so the next sign-in is where
 * a change of information under Art. 13(3) can actually reach somebody.
 *
 * **It notifies; it does not gate.** Nothing on the page behind it is withheld, nothing is
 * re-collected, and the person can ignore it indefinitely — a `severity="info"` banner rather than
 * a modal, deliberately. Freezing somebody's own data pending a click would be a worse outcome
 * than their reading the notice a week late.
 *
 * Renders nothing at all when there is nothing waiting, which is the ordinary case.
 */
export default function NoticeUpdateBanner() {
  const status = useNoticeStatus();
  const acknowledge = useAcknowledgeNotice();
  const [expanded, setExpanded] = useState(false);

  const pending = status.data?.pendingVersion;
  if (!pending) {
    return null;
  }

  return (
    <Alert severity="info" sx={{ mb: 2 }}>
      <AlertTitle>We've updated what we tell you about your data</AlertTitle>
      <Stack spacing={1.5} sx={{ mt: 1 }}>
        <ErrorNotice message={acknowledge.isError ? apiErrorMessage(acknowledge.error) : null} />

        <Collapse in={expanded} unmountOnExit>
          <TransparencyNoticeText />
        </Collapse>

        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          <Button size="small" onClick={() => setExpanded((open) => !open)}>
            {expanded ? "Hide the notice" : "Read the notice"}
          </Button>
          <Button
            size="small"
            variant="contained"
            disabled={acknowledge.isPending}
            onClick={() => acknowledge.mutate(pending)}
          >
            {acknowledge.isPending ? "Recording…" : "I've read it"}
          </Button>
        </Stack>
      </Stack>
    </Alert>
  );
}
