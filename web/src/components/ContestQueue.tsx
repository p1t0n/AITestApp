import { useState } from "react";
import {
  Alert,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from "@mui/material";
import type { ContestOutcome, ContestQueueItem } from "../api";

/**
 * Why this queue exists at all, stated on the screen rather than in a manual. A Service Manager
 * reviewing a contested score is not doing customer service — they are the Art. 22(3) safeguard,
 * and if nobody actually reads what the person wrote then the legal basis the whole scan rests on
 * is not being honoured. That is worth one sentence in front of them.
 */
export const CONTEST_QUEUE_NOTE =
  "Software scored these people automatically and they have asked for a person to look. Reading " +
  "what they wrote and deciding is the safeguard that makes the automated scoring lawful — it is " +
  "not a formality, and there is no right answer you are being steered towards.";

/**
 * Scores waiting for a human (P1T-189). Sits beside the claim queue: one page a Service Manager
 * goes to for the decisions only a person can make.
 */
export default function ContestQueue({
  contests,
  loading,
  onReview,
  busy,
}: {
  contests: ContestQueueItem[] | undefined;
  loading: boolean;
  onReview: (item: ContestQueueItem, outcome: ContestOutcome, response: string) => void;
  busy: boolean;
}) {
  const [reviewing, setReviewing] = useState<ContestQueueItem | null>(null);

  return (
    <Stack spacing={2} sx={{ mb: 4 }}>
      <Typography variant="h6" component="h2">
        Contested scores
      </Typography>

      <Alert severity="info">{CONTEST_QUEUE_NOTE}</Alert>

      <Paper>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Person</TableCell>
              <TableCell>What the software said</TableCell>
              <TableCell>What they say</TableCell>
              <TableCell align="right">Review</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={4}>Loading…</TableCell>
              </TableRow>
            )}
            {contests?.map((item) => (
              <TableRow key={item.scoringCandidateId} hover>
                <TableCell>
                  <Stack spacing={0.5}>
                    <span>{item.expertName}</span>
                    <Typography variant="caption" color="text.secondary">
                      {item.jobDescription}
                    </Typography>
                  </Stack>
                </TableCell>
                <TableCell>
                  <Stack spacing={0.5}>
                    {item.score !== null && (
                      <Chip
                        size="small"
                        label={`${item.score}${item.band ? ` · ${item.band}` : ""}`}
                        sx={{ alignSelf: "flex-start" }}
                      />
                    )}
                    <Typography variant="caption" color="text.secondary">
                      {item.rationale ?? "No rationale was recorded."}
                    </Typography>
                  </Stack>
                </TableCell>
                <TableCell>
                  {item.view ?? (
                    <Typography variant="caption" color="text.secondary">
                      They asked for a person to look, without saying more. That is their right on
                      its own.
                    </Typography>
                  )}
                </TableCell>
                <TableCell align="right">
                  <Button onClick={() => setReviewing(item)} disabled={busy}>
                    Review
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {contests?.length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>Nothing waiting.</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Paper>

      {reviewing && (
        <ReviewDialog
          item={reviewing}
          busy={busy}
          onClose={() => setReviewing(null)}
          onReview={(outcome, response) => {
            onReview(reviewing, outcome, response);
            setReviewing(null);
          }}
        />
      )}
    </Stack>
  );
}

function ReviewDialog({
  item,
  busy,
  onClose,
  onReview,
}: {
  item: ContestQueueItem;
  busy: boolean;
  onClose: () => void;
  onReview: (outcome: ContestOutcome, response: string) => void;
}) {
  const [response, setResponse] = useState("");

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Review {item.expertName}&rsquo;s score</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          {item.view
            ? `They said: “${item.view}”`
            : "They asked for a person to look without saying more."}
        </DialogContentText>

        <TextField
          label="What you say back to them"
          value={response}
          onChange={(event) => setResponse(event.target.value)}
          helperText="They will read this. It is the evidence that somebody engaged with what they wrote."
          multiline
          minRows={3}
          fullWidth
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button disabled={busy} onClick={() => onReview("upheld", response)}>
          The score stands
        </Button>
        <Button variant="contained" disabled={busy} onClick={() => onReview("overturned", response)}>
          I disagree with the score
        </Button>
      </DialogActions>
    </Dialog>
  );
}
