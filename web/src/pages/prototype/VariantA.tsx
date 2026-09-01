/**
 * PROTOTYPE — throwaway. Variant A — "The record".
 *
 * Single column, document rhythm. State is a sentence, not a chip. Every right is a labelled row
 * with its action as a text button at the end of the row, so the page reads as a file about you
 * rather than a control panel. Delete is pushed to the bottom behind a rule and a heading of its
 * own — separation by position and typography, not by colour.
 */
import { useState } from "react";
import { Box, Button, Divider, Paper, Stack, TextField, Typography } from "@mui/material";
import PageHeader from "../../components/PageHeader";
import { derive, type Actions, type PrivacyState } from "./privacyState";

function Row({
  label,
  children,
  action,
}: {
  label: string;
  children: React.ReactNode;
  action?: React.ReactNode;
}) {
  return (
    <>
      <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ py: 2 }}>
        <Typography variant="subtitle2" sx={{ width: { sm: 200 }, flexShrink: 0, pt: 0.25 }}>
          {label}
        </Typography>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="body2" color="text.secondary" component="div">
            {children}
          </Typography>
        </Box>
        {action && <Box sx={{ flexShrink: 0, pt: 0.25 }}>{action}</Box>}
      </Stack>
      <Divider />
    </>
  );
}

export default function VariantA({ s, on }: { s: PrivacyState; on: Actions }) {
  const d = derive(s);
  const [word, setWord] = useState("");

  const sentence = !s.ownsRow
    ? s.claimPending
      ? "Your claim on a profile is waiting for a Service Manager to approve it. Until then there is nothing here to show you."
      : "You do not have a profile yet."
    : d.paused && d.expiring
      ? `Your profile is paused, and the record we hold expires on ${s.expiresOn} — in ${s.daysToExpiry} days.`
      : d.paused
        ? "Your profile is paused. Service Managers can see it is paused; nobody is offered it for work."
        : d.expiring
          ? `Your profile is active, and the record we hold expires on ${s.expiresOn} — in ${s.daysToExpiry} days. Signing in keeps it.`
          : `Your profile is active and can be offered for work. We keep it until ${s.expiresOn} unless you use it before then.`;

  return (
    <PageHeader title="Privacy and data" width="content">
      <Paper variant="outlined" sx={{ p: 3, mb: 3 }}>
        <Typography variant="body1">{sentence}</Typography>
        {!d.scannable && s.ownsRow && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Because this profile was created for you rather than by you, it is not put through
            automated matching. Claiming it changes that.
          </Typography>
        )}
      </Paper>

      <Typography variant="h6" sx={{ mb: 1 }}>
        What we hold about you
      </Typography>
      <Divider />

      <Row label="Your CV" action={<Button size="small" href="#">Edit</Button>}>
        Contact details, summary, spoken languages, skills, qualifications, work history and
        availability — everything you entered, plus anything a Service Manager corrected.
      </Row>

      <Row label="Search index">
        A machine-readable version of your CV text, used so a Service Manager can find you by
        describing what they need rather than by keyword.
      </Row>

      <Row label="Assessments">
        {s.scored.length === 0
          ? "Nothing yet."
          : s.scored.map((x) => (
              <Box key={x.job} sx={{ mb: 1.5 }}>
                <Typography variant="body2" color="text.primary">
                  {x.job} — {x.score}/100, {x.band}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  {x.rationale}
                </Typography>
                <Button size="small" sx={{ mt: 0.5, px: 0 }} onClick={() => on.contest(x.job)}>
                  Ask a person to review this
                </Button>
              </Box>
            ))}
      </Row>

      <Row label="Who sees it">
        Service Managers at this company, and Google — whose Gemini models produce the search index
        and the assessments above.
      </Row>

      <Row label="How it was decided">
        A model reads your CV against the job description and produces a score, a band and the
        reasoning shown above. A Service Manager decides who is put forward. The score decides who
        they look at first.
      </Row>

      <Row label="Why we may hold it">
        {s.basis === "contract"
          ? "You asked to be considered for work, and we cannot do that without holding your CV."
          : "We hold it in our legitimate interest as a staffing bench. You can object at any time."}
      </Row>

      <Row
        label="A copy of your data"
        action={
          <Button size="small" variant="outlined" onClick={on.exportData}>
            Download JSON
          </Button>
        }
      >
        {d.canExport === "right"
          ? "Machine-readable, everything you gave us. This is your right to data portability."
          : "Machine-readable, everything you gave us. We offer this as a courtesy — for this profile it is not a portability right."}
      </Row>

      <Row
        label="Being offered for work"
        action={
          d.paused ? (
            <Button size="small" variant="outlined" onClick={on.unpause}>
              Resume
            </Button>
          ) : (
            <Button size="small" variant="outlined" onClick={on.pause}>
              Pause
            </Button>
          )
        }
      >
        Pausing hides you from search and matching. Nothing is deleted and you can undo it whenever
        you like.
      </Row>

      {d.canObject && (
        <Row
          label="Objecting"
          action={
            <Button size="small" variant="outlined" color="warning" onClick={on.object}>
              Object
            </Button>
          }
        >
          You can object to us holding this profile at all. We will not weigh it against our own
          interest — objecting removes your data.
        </Row>
      )}

      <Box sx={{ mt: 6 }}>
        <Divider sx={{ mb: 3 }} />
        <Typography variant="h6" sx={{ mb: 1 }}>
          Deleting everything
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          This removes your CV, your search index, your assessments and your sign-in. It cannot be
          undone, and we have no way to contact you afterwards. If you only want to stop being
          offered for work, <b>pause</b> above instead — that is reversible.
        </Typography>
        <Stack direction={{ xs: "column", sm: "row" }} spacing={2} alignItems={{ sm: "flex-start" }}>
          <TextField
            size="small"
            label="Your control word"
            value={word}
            onChange={(e) => setWord(e.target.value)}
            sx={{ maxWidth: 260 }}
          />
          <Button
            variant="outlined"
            color="error"
            disabled={word.length === 0}
            onClick={() => on.deleteAll(word)}
          >
            Delete everything
          </Button>
        </Stack>
      </Box>
    </PageHeader>
  );
}
