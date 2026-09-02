import { useState } from "react";
import {
  Alert,
  Button,
  Chip,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { apiErrorMessage, useContestScore, useMyAccessView } from "../api";
import { ErrorNotice } from "./ErrorNotice";

/**
 * What Art. 22(3) actually offers somebody, in words rather than in article numbers. The middle
 * clause is the one that matters and the one people do not expect: they can disagree and a person
 * has to look.
 */
export const CONTEST_RIGHT =
  "Software scored you against these jobs, and that score decides who a Service Manager sees " +
  "first. You can ask for a person to look at any of them, say why you disagree, and have the " +
  "outcome reconsidered.";

/**
 * The Expert's contest control (P1T-189). It hangs off the access view's assessments because you
 * can only contest what you can see — the score and the rationale written about you are shown in
 * full, and this is the button beside them.
 */
export default function ContestableScores() {
  const access = useMyAccessView();
  const contest = useContestScore();
  const [contesting, setContesting] = useState<string | null>(null);
  const [view, setView] = useState("");

  const assessments = access.data?.derived.assessments ?? [];
  const scored = assessments.filter((a) => a.score !== null);

  if (access.isError || scored.length === 0) {
    return null;
  }

  return (
    <Paper variant="outlined" sx={{ p: 3, mt: 3 }}>
      <Stack spacing={2}>
        <Typography variant="h6" component="h2">
          How software has scored you
        </Typography>

        <Typography variant="body2" color="text.secondary">
          {CONTEST_RIGHT}
        </Typography>

        <ErrorNotice message={contest.isError ? apiErrorMessage(contest.error) : null} />
        {contest.isSuccess && (
          <Alert severity="success">
            Asked. A Service Manager will look at it, and what they decide will appear here.
          </Alert>
        )}

        {scored.map((assessment) => (
          <Stack key={assessment.sourceId} spacing={1} sx={{ py: 1 }}>
            <Stack direction="row" spacing={1} alignItems="center">
              <Chip
                size="small"
                label={`${assessment.score}${assessment.band ? ` · ${assessment.band}` : ""}`}
              />
              <Typography variant="body2" color="text.secondary">
                {assessment.source}
              </Typography>
            </Stack>

            {assessment.rationale && (
              <Typography variant="body2">{assessment.rationale}</Typography>
            )}

            {contesting === assessment.sourceId ? (
              <Stack spacing={1}>
                <TextField
                  label="Why you disagree (optional)"
                  value={view}
                  onChange={(event) => setView(event.target.value)}
                  helperText="A person will read this. You can also just ask, without explaining."
                  multiline
                  minRows={2}
                  fullWidth
                />
                <Stack direction="row" spacing={1}>
                  <Button
                    variant="contained"
                    disabled={contest.isPending}
                    onClick={() =>
                      contest.mutate(
                        {
                          scoringCandidateId: assessment.sourceId,
                          view: view.trim() === "" ? undefined : view,
                        },
                        { onSuccess: () => { setContesting(null); setView(""); } },
                      )
                    }
                  >
                    Ask for a person to look
                  </Button>
                  <Button onClick={() => setContesting(null)}>Cancel</Button>
                </Stack>
              </Stack>
            ) : (
              <Stack direction="row">
                <Button size="small" onClick={() => setContesting(assessment.sourceId)}>
                  Ask for a person to look at this
                </Button>
              </Stack>
            )}
          </Stack>
        ))}
      </Stack>
    </Paper>
  );
}
