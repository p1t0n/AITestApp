/**
 * PROTOTYPE — throwaway. Variant C — "Intents".
 *
 * State is a full-width banner strip, loud enough to read without looking for it. Below it the page
 * is not data at all — it is a list of things you might want to do, each expanding in place. The
 * data we hold is itself one of the intents rather than the page's subject.
 *
 * Separation of pause from delete is by *category*: the reversible intents sit in one group under
 * "Change what happens", the irreversible ones in a separate group under "End it", each with a
 * different frame and an explicit consequence line. Delete asks for the control word inline, inside
 * its own expanded panel, so the confirm is never a modal you can dismiss by reflex.
 */
import { useState } from "react";
import { Box, Button, Collapse, Paper, Stack, TextField, Typography } from "@mui/material";
import PageHeader from "../../components/PageHeader";
import { derive, type Actions, type PrivacyState } from "./privacyState";

function Intent({
  title,
  blurb,
  consequence,
  tone = "normal",
  children,
}: {
  title: string;
  blurb: string;
  consequence?: string;
  tone?: "normal" | "danger";
  children: React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2.5,
        borderColor: tone === "danger" ? "error.main" : undefined,
        borderStyle: tone === "danger" ? "dashed" : "solid",
      }}
    >
      <Stack direction="row" alignItems="flex-start" spacing={2}>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="subtitle1" color={tone === "danger" ? "error" : "text.primary"}>
            {title}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {blurb}
          </Typography>
          {consequence && (
            <Typography
              variant="caption"
              sx={{ display: "block", mt: 0.75, fontWeight: 600 }}
              color={tone === "danger" ? "error" : "text.secondary"}
            >
              {consequence}
            </Typography>
          )}
        </Box>
        <Button
          size="small"
          variant={tone === "danger" ? "outlined" : "contained"}
          color={tone === "danger" ? "error" : "primary"}
          onClick={() => setOpen((v) => !v)}
          sx={{ flexShrink: 0 }}
        >
          {open ? "Close" : tone === "danger" ? "Continue" : "Open"}
        </Button>
      </Stack>
      <Collapse in={open}>
        <Box sx={{ mt: 2 }}>{children}</Box>
      </Collapse>
    </Paper>
  );
}

export default function VariantC({ s, on }: { s: PrivacyState; on: Actions }) {
  const d = derive(s);
  const [word, setWord] = useState("");

  const banner = !s.ownsRow
    ? {
        bg: "info.main",
        head: s.claimPending ? "Claim pending" : "No profile yet",
        body: s.claimPending
          ? "A Service Manager is reviewing your claim. Nothing is held under your name until they approve it."
          : "Nothing is held under your name.",
      }
    : d.paused
      ? {
          bg: "warning.main",
          head: d.expiring ? `Paused · expires in ${s.daysToExpiry} days` : "Paused",
          body: "You are hidden from search and matching. Nothing has been deleted, and you can resume whenever you like.",
        }
      : d.expiring
        ? {
            bg: "warning.main",
            head: `Active · expires in ${s.daysToExpiry} days`,
            body: `We delete records nobody uses. Yours goes on ${s.expiresOn} — but you have just kept it by signing in.`,
          }
        : {
            bg: "success.main",
            head: d.scannable ? "Active and being matched" : "Active, not being matched",
            body: d.scannable
              ? `Service Managers can find you and you are included in automated matching. Kept until ${s.expiresOn}.`
              : `Service Managers can find you, but this profile is not put through automated matching because it was created for you rather than by you.`,
          };

  return (
    <PageHeader title="Privacy and data" width="content">
      <Paper
        sx={{
          p: 2.5,
          mb: 4,
          bgcolor: banner.bg,
          color: "#fff",
          borderRadius: 1,
        }}
        elevation={0}
      >
        <Typography variant="h6" sx={{ color: "inherit" }}>
          {banner.head}
        </Typography>
        <Typography variant="body2" sx={{ color: "inherit", opacity: 0.92 }}>
          {banner.body}
        </Typography>
      </Paper>

      <Typography variant="overline" color="text.secondary">
        Look at what we have
      </Typography>
      <Stack spacing={1.5} sx={{ mt: 1, mb: 4 }}>
        <Intent
          title="See everything we hold about you"
          blurb="Your CV, the search index built from it, every assessment a model has written about you, who sees it and how long we keep it."
        >
          <Stack spacing={2}>
            {s.scored.map((x) => (
              <Paper key={x.job} variant="well" sx={{ p: 2 }}>
                <Typography variant="subtitle2">
                  {x.job} — {x.score}/100, {x.band}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  {x.rationale}
                </Typography>
                <Button size="small" sx={{ mt: 1 }} onClick={() => on.contest(x.job)}>
                  Ask a person to review this
                </Button>
              </Paper>
            ))}
            <Typography variant="body2" color="text.secondary">
              Seen by Service Managers at this company, and by Google — whose Gemini models produce
              the index and the assessments above.
            </Typography>
          </Stack>
        </Intent>

        <Intent
          title="Take a copy with you"
          blurb={
            d.canExport === "right"
              ? "Everything you gave us, as JSON. This is your data portability right."
              : "Everything you gave us, as JSON. We offer this as a courtesy — for this profile it is not a portability right."
          }
        >
          <Button variant="outlined" onClick={on.exportData}>
            Download JSON
          </Button>
        </Intent>
      </Stack>

      <Typography variant="overline" color="text.secondary">
        Change what happens
      </Typography>
      <Stack spacing={1.5} sx={{ mt: 1, mb: 4 }}>
        <Intent
          title={d.paused ? "Start being offered for work again" : "Stop being offered for work"}
          blurb={
            d.paused
              ? "Put yourself back into search and matching."
              : "Hide yourself from search and matching. Your CV stays exactly as it is."
          }
          consequence="Reversible — you can switch this back any time."
        >
          <Button variant="contained" onClick={d.paused ? on.unpause : on.pause}>
            {d.paused ? "Resume" : "Pause"}
          </Button>
        </Intent>
      </Stack>

      <Typography variant="overline" color="error">
        End it
      </Typography>
      <Stack spacing={1.5} sx={{ mt: 1 }}>
        {d.canObject && (
          <Intent
            tone="danger"
            title="Object to us holding this profile"
            blurb="You did not put yourself here. You can tell us to stop."
            consequence="Not reversible — we do not weigh objections, so this deletes your data."
          >
            <Button variant="outlined" color="error" onClick={on.object}>
              Object and delete
            </Button>
          </Intent>
        )}

        <Intent
          tone="danger"
          title="Delete everything"
          blurb="Your CV, your search index, your assessments and your sign-in."
          consequence="Not reversible. We have no way to contact you afterwards, so nothing can be restored."
        >
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              If you only want to stop being offered for work, use <b>Stop being offered</b> above —
              that keeps your data and can be undone.
            </Typography>
            <TextField
              size="small"
              label="Type your control word to confirm"
              value={word}
              onChange={(e) => setWord(e.target.value)}
              sx={{ maxWidth: 300 }}
            />
            <Button
              variant="contained"
              color="error"
              disabled={word.length === 0}
              onClick={() => on.deleteAll(word)}
              sx={{ alignSelf: "flex-start" }}
            >
              Delete everything permanently
            </Button>
          </Stack>
        </Intent>
      </Stack>
    </PageHeader>
  );
}
