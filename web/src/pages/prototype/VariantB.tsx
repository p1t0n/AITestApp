/**
 * PROTOTYPE — throwaway. Variant B — "Status card and accordions".
 *
 * Two columns. Right: a sticky card that is the page's primary affordance — state as chips, and the
 * two safe actions. Left: the data we hold, collapsed into accordions so the page opens short.
 * Delete and object are exiled to a closed disclosure at the bottom of the left column — separation
 * by distance and by requiring a deliberate open, so the destructive control is never adjacent to
 * the reversible one.
 */
import { useState } from "react";
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Chip,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import PageHeader from "../../components/PageHeader";
import { derive, type Actions, type PrivacyState } from "./privacyState";

export default function VariantB({ s, on }: { s: PrivacyState; on: Actions }) {
  const d = derive(s);
  const [openDanger, setOpenDanger] = useState(false);
  const [word, setWord] = useState("");

  return (
    <PageHeader title="Privacy and data" width="wide">
      <Stack direction={{ xs: "column", md: "row" }} spacing={3} alignItems="flex-start">
        {/* LEFT — the data, collapsed */}
        <Box sx={{ flexGrow: 1, minWidth: 0, width: "100%" }}>
          {!s.ownsRow && (
            <Alert severity="info" sx={{ mb: 2 }}>
              {s.claimPending
                ? "A Service Manager is reviewing your claim. Nothing is held under your name yet."
                : "You have no profile yet."}
            </Alert>
          )}

          <Accordion defaultExpanded disableGutters variant="outlined">
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="subtitle2">Your CV</Typography>
            </AccordionSummary>
            <AccordionDetails>
              <Typography variant="body2" color="text.secondary">
                Contact details, summary, languages, skills, qualifications, work history and
                availability. You maintain it; a Service Manager may also correct it.
              </Typography>
            </AccordionDetails>
          </Accordion>

          <Accordion disableGutters variant="outlined">
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="subtitle2">
                Assessments{" "}
                <Typography component="span" variant="body2" color="text.secondary">
                  ({s.scored.length})
                </Typography>
              </Typography>
            </AccordionSummary>
            <AccordionDetails>
              <Stack spacing={2}>
                {s.scored.map((x) => (
                  <Paper key={x.job} variant="well" sx={{ p: 2 }}>
                    <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                      <Typography variant="subtitle2">{x.job}</Typography>
                      <Chip size="small" label={`${x.score}/100 · ${x.band}`} />
                    </Stack>
                    <Typography variant="body2" color="text.secondary">
                      {x.rationale}
                    </Typography>
                    <Button size="small" sx={{ mt: 1 }} onClick={() => on.contest(x.job)}>
                      Ask a person to review this
                    </Button>
                  </Paper>
                ))}
              </Stack>
            </AccordionDetails>
          </Accordion>

          <Accordion disableGutters variant="outlined">
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="subtitle2">Search index</Typography>
            </AccordionSummary>
            <AccordionDetails>
              <Typography variant="body2" color="text.secondary">
                A machine-readable version of your CV text, so a Service Manager can find you by
                describing a need rather than guessing keywords.
              </Typography>
            </AccordionDetails>
          </Accordion>

          <Accordion disableGutters variant="outlined">
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Typography variant="subtitle2">Who sees it, and how decisions are made</Typography>
            </AccordionSummary>
            <AccordionDetails>
              <Typography variant="body2" color="text.secondary" paragraph>
                Service Managers at this company, and Google — whose Gemini models build the search
                index and write the assessments.
              </Typography>
              <Typography variant="body2" color="text.secondary">
                A model reads your CV against a job description and produces a score, a band and the
                reasoning. A Service Manager chooses who to put forward; the score decides who they
                see first.
              </Typography>
            </AccordionDetails>
          </Accordion>

          {/* The danger zone: far from the card, and closed by default. */}
          <Box sx={{ mt: 5 }}>
            {!openDanger ? (
              <Button color="error" onClick={() => setOpenDanger(true)}>
                Close your account…
              </Button>
            ) : (
              <Paper variant="outlined" sx={{ p: 2.5, borderColor: "error.main" }}>
                <Typography variant="subtitle2" color="error" sx={{ mb: 1 }}>
                  Close your account
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                  Removes your CV, search index, assessments and sign-in. Irreversible, and we cannot
                  contact you afterwards. To stop being offered for work without losing anything, use{" "}
                  <b>Pause</b> in the card instead.
                </Typography>
                <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                  <TextField
                    size="small"
                    label="Your control word"
                    value={word}
                    onChange={(e) => setWord(e.target.value)}
                    sx={{ maxWidth: 240 }}
                  />
                  <Button
                    variant="contained"
                    color="error"
                    disabled={word.length === 0}
                    onClick={() => on.deleteAll(word)}
                  >
                    Delete everything
                  </Button>
                  <Button onClick={() => setOpenDanger(false)}>Cancel</Button>
                </Stack>
                {d.canObject && (
                  <Box sx={{ mt: 2 }}>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                      Or object to us holding this profile at all. We do not weigh an objection —
                      it removes your data.
                    </Typography>
                    <Button color="warning" onClick={on.object}>
                      Object to this processing
                    </Button>
                  </Box>
                )}
              </Paper>
            )}
          </Box>
        </Box>

        {/* RIGHT — the sticky status card, the page's primary affordance */}
        <Paper
          variant="outlined"
          sx={{ p: 2.5, width: { xs: "100%", md: 320 }, flexShrink: 0, position: { md: "sticky" }, top: { md: 88 } }}
        >
          <Typography variant="subtitle2" sx={{ mb: 1.5 }}>
            Your profile
          </Typography>

          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mb: 2 }}>
            {!s.ownsRow ? (
              <Chip size="small" color="info" label={s.claimPending ? "Claim pending" : "No profile"} />
            ) : d.paused ? (
              <Chip size="small" color="warning" label="Paused" />
            ) : (
              <Chip size="small" color="success" label="Active" />
            )}
            {d.expiring && <Chip size="small" color="warning" variant="outlined" label={`Expires in ${s.daysToExpiry}d`} />}
            {!d.scannable && s.ownsRow && <Chip size="small" variant="outlined" label="Not matched" />}
          </Stack>

          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {d.paused
              ? "Hidden from search and matching. Nothing has been deleted."
              : d.scannable
                ? "Visible to Service Managers and included in matching."
                : "Visible to Service Managers. Not included in automated matching."}
          </Typography>

          <Stack spacing={1}>
            {d.paused ? (
              <Button fullWidth variant="contained" onClick={on.unpause}>
                Resume being offered
              </Button>
            ) : (
              <Button fullWidth variant="outlined" onClick={on.pause}>
                Pause being offered
              </Button>
            )}
            <Button fullWidth variant="outlined" onClick={on.exportData}>
              Download my data
            </Button>
            <Typography variant="caption" color="text.secondary">
              {d.canExport === "right"
                ? "JSON. Your portability right."
                : "JSON. Offered as a courtesy."}
            </Typography>
          </Stack>

          <Typography variant="caption" color="text.secondary" sx={{ display: "block", mt: 2 }}>
            We keep your record until {s.expiresOn}. Using the site keeps it.
          </Typography>
        </Paper>
      </Stack>
    </PageHeader>
  );
}
