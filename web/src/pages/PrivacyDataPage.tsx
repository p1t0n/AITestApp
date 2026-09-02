import { useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import {
  Box,
  Button,
  CircularProgress,
  Divider,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import {
  apiErrorMessage,
  useContestScore,
  useDownloadMyExport,
  useEraseMyAccount,
  useMyAccessView,
  useMyVisibility,
  useSetMyVisibility,
  type AccessView,
} from "../api";
import { AgentMarkdown } from "../components/agent/AgentMarkdown";
import { ErrorNotice } from "../components/ErrorNotice";
import NoticeUpdateBanner from "../components/NoticeUpdateBanner";
import PageHeader, { PageContainer } from "../components/PageHeader";
import RedeemClaimCode from "../components/RedeemClaimCode";
import { clearSession } from "../auth/session";

/**
 * One labelled row of the record. A definition-list rhythm: the label on the left, prose in the
 * middle, and this right's action as a button at the end of its own row.
 *
 * <p>Carries a `row-<label>` hook so a test can scope to one right: two rows hold a control-word
 * field — objecting and deleting are the same act reached two ways — and a page-wide query cannot
 * tell them apart. The labels are the page's structure, so renaming one is a visible change rather
 * than a refactor.</p>
 */
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
      <Stack
        direction={{ xs: "column", sm: "row" }}
        spacing={2}
        sx={{ py: 2 }}
        // Addressable by its label, so a test can scope to one right rather than to the page. Two
        // rows legitimately carry a control-word field — objecting and deleting are the same act
        // reached two ways — and a page-wide query cannot tell them apart.
        data-testid={`row-${label}`}
      >
        <Typography variant="subtitle2" sx={{ width: { sm: 210 }, flexShrink: 0, pt: 0.25 }}>
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

/**
 * Everything the service holds about one person, and every right they have over it — Variant A,
 * "The record", chosen by the P1T-175 prototype run and rewritten here properly (P1T-191).
 *
 * <p><b>Two properties of A are load-bearing and must survive any later edit.</b></p>
 *
 * <p><b>One source of truth about state.</b> Every fact about the state is prose in this one
 * column, so there is no second surface that can drift out of agreement with the page. That is
 * exactly what killed Variant B, whose status card claimed "Visible to Service Managers" and
 * offered <em>Pause</em> and <em>Download my data</em> while its own banner said nothing was held
 * yet. <b>Do not add a status card, a sidebar or a sticky summary.</b> The accepted cost is that
 * this page tells you your state only if you read the opening sentence — that is the same property,
 * not a defect to fix.</p>
 *
 * <p><b>The distance between pause and delete is the separation.</b> The page is long, and the
 * length is the mechanism (P1T-171 chose two controls precisely so nobody deletes when they meant
 * to pause, and this service has no email to undo it with). <b>Do not shorten the page in a way
 * that brings the two controls near each other</b> — no accordions collapsing the body, no moving
 * delete up into a toolbar.</p>
 */
export default function PrivacyDataPage() {
  const visibility = useMyVisibility();
  const access = useMyAccessView();

  // Owning no record is a legitimate state, not an error (P1T-182), and it is also the state in
  // which almost every row below has nothing to say — so the page degrades to one accurate
  // sentence rather than to a column of empty rows.
  if (visibility.isLoading || access.isLoading) {
    return (
      <PageContainer width="content">
        <CircularProgress />
      </PageContainer>
    );
  }

  if (visibility.isError || !visibility.data || access.isError || !access.data) {
    return <NoRecordYet />;
  }

  return (
    <TheRecord
      access={access.data}
      paused={visibility.data.hidden}
      pausedSince={visibility.data.hiddenSince}
    />
  );
}

/** The claim-pending / no-profile degradation: one sentence, and the two acts still available. */
function NoRecordYet() {
  return (
    <PageHeader title="Privacy and data" width="content">
      <NoticeUpdateBanner />

      <Paper variant="outlined" sx={{ p: 3, mb: 3 }}>
        <Typography variant="body1">
          There is nothing held under your name yet, so there is nothing here to show you.
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          If a Service Manager already had a record for you, a person has to confirm it is yours
          before you can see it. This page fills in once it is.
        </Typography>
      </Paper>

      <RedeemClaimCode />

      <DeleteEverything holdsRecord={false} />
    </PageHeader>
  );
}

function TheRecord({
  access,
  paused,
  pausedSince,
}: {
  access: AccessView;
  paused: boolean;
  pausedSince: string | null;
}) {
  const setVisibility = useSetMyVisibility();
  const exportData = useDownloadMyExport();
  const contest = useContestScore();

  const scored = access.derived.assessments.filter((a) => a.score !== null);
  const objecting = access.basis === "LegitimateInterest";
  const expiresOn = access.expiresAt ? new Date(access.expiresAt).toLocaleDateString() : null;

  return (
    <PageHeader title="Privacy and data" width="content">
      <NoticeUpdateBanner />

      {/* The state, as a sentence. Not a chip and not a banner: the combinations — paused and
          expiring at once, legitimate interest and contestable at once — have to read as one
          coherent statement, and two chips competing for the same slot is where B failed. */}
      <Paper variant="outlined" sx={{ p: 3, mb: 3 }}>
        <Typography variant="body1">
          <StateSentence paused={paused} pausedSince={pausedSince} expiresOn={expiresOn} expiringSoon={access.expiringSoon} />
        </Typography>
        {objecting && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            Because this record was created for you rather than by you, it is not put through
            automated matching at all. Claiming it would change that.
          </Typography>
        )}
      </Paper>

      <ErrorNotice
        message={
          setVisibility.isError || exportData.isError || contest.isError
            ? apiErrorMessage(setVisibility.error ?? exportData.error ?? contest.error)
            : null
        }
        sx={{ mb: 2 }}
      />

      <Typography variant="h6" component="h2" sx={{ mb: 1 }}>
        What we hold about you
      </Typography>
      <Divider />

      <Row
        label="Your CV"
        action={
          <Button size="small" component={RouterLink} to="/me/cv">
            Edit
          </Button>
        }
      >
        {access.dataCategories[0]}
      </Row>

      <Row label="Everything in it">
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {access.dataCategories.slice(1).map((category) => (
            <li key={category}>{category}</li>
          ))}
        </Box>
      </Row>

      <Row label="The search index">{access.derived.searchIndexNote}</Row>

      <Row label="Assessments">
        {scored.length === 0 ? (
          "Nothing yet — software has not scored you against a job."
        ) : (
          <Stack spacing={2}>
            {scored.map((assessment) => (
              <Box key={assessment.sourceId}>
                <Typography variant="body2" color="text.primary">
                  {assessment.source} — {assessment.score}/100
                  {assessment.band ? `, ${assessment.band}` : ""}
                </Typography>
                {assessment.rationale && (
                  <Typography variant="body2" color="text.secondary">
                    {assessment.rationale}
                  </Typography>
                )}
                {assessment.matchAnswer && (
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                    {assessment.matchAnswer}
                  </Typography>
                )}
                {/* Contesting sits on the row it is about (P1T-189): you can only contest what you
                    can see, and the thing being contested is right here. */}
                <Button
                  size="small"
                  sx={{ mt: 0.5, px: 0 }}
                  disabled={contest.isPending}
                  onClick={() => contest.mutate({ scoringCandidateId: assessment.sourceId })}
                >
                  Ask a person to review this
                </Button>
              </Box>
            ))}
          </Stack>
        )}
      </Row>

      <Row label="What we use it for">
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {access.purposes.map((purpose) => (
            <li key={purpose}>{purpose}</li>
          ))}
        </Box>
      </Row>

      <Row label="Who sees it">
        <Stack spacing={1}>
          {access.recipients.map((recipient) => (
            <Box key={recipient.recipient}>
              <Typography variant="body2" color="text.primary">
                {recipient.recipient}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {recipient.why}
              </Typography>
            </Box>
          ))}
        </Stack>
      </Row>

      <Row label="How the scoring works">
        <AgentMarkdown text={access.art22Logic} />
      </Row>

      <Row label="Why we may hold it">
        {access.basis === "ContractNecessity"
          ? "You asked to be considered for work, and we cannot do that without holding your CV — steps taken at your own request before a contract."
          : "We hold it in our own legitimate interest as a staffing bench. You can object at any time, and objecting is further down this page."}
        {access.source && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            {access.source}
          </Typography>
        )}
      </Row>

      <Row label="How long we keep it">
        {access.retention}
        {expiresOn && (
          <Typography variant="body2" color="text.primary" sx={{ mt: 1 }}>
            As it stands, this record is due to be deleted on {expiresOn}.
          </Typography>
        )}
      </Row>

      <Row
        label="A copy of your data"
        action={
          <Button
            size="small"
            variant="outlined"
            disabled={exportData.isPending}
            onClick={() => exportData.mutate()}
          >
            Download JSON
          </Button>
        }
      >
        {access.export === "Right"
          ? "Machine-readable, everything you gave us. This is your right to data portability."
          : "Machine-readable, everything you gave us. We offer it as a courtesy — for a record somebody else created, portability is not a right you have."}
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          It carries what you provided. The scores and rationales above are not in it — those are
          our conclusions about you rather than your own data, and you read them here instead.
        </Typography>
      </Row>

      <Row
        label="Being offered for work"
        action={
          <Button
            size="small"
            variant="outlined"
            disabled={setVisibility.isPending}
            onClick={() => setVisibility.mutate(!paused)}
          >
            {paused ? "Resume" : "Pause"}
          </Button>
        }
      >
        Pausing hides you from search and matching. Nothing is deleted, and you can undo it whenever
        you like.
      </Row>

      {/* Inline, in the same rhythm as every other right. Burying objection was Variant B's second
          defect, and it is the only exit somebody on legitimate interest has. */}
      {objecting && <Objecting />}

      <Row label="Complaining about any of this">{access.complaintRight}</Row>

      <DeleteEverything holdsRecord />
    </PageHeader>
  );
}

function StateSentence({
  paused,
  pausedSince,
  expiresOn,
  expiringSoon,
}: {
  paused: boolean;
  pausedSince: string | null;
  expiresOn: string | null;
  expiringSoon: boolean;
}) {
  const since = pausedSince ? ` since ${new Date(pausedSince).toLocaleDateString()}` : "";

  // The combinations are the point, so they are one sentence rather than two stacked warnings.
  if (paused && expiringSoon && expiresOn) {
    return (
      <>
        Your record is paused{since}, and it is due to be deleted on {expiresOn}. Reading this page
        counts as using it, so that date has already moved.
      </>
    );
  }

  if (paused) {
    return (
      <>
        Your record is paused{since}. Service Managers can see that it is paused; nobody is offered
        it for work.
      </>
    );
  }

  if (expiringSoon && expiresOn) {
    return (
      <>
        Your record is active and can be offered for work, and it was due to be deleted on{" "}
        {expiresOn}. Reading this page counts as using it, so that date has already moved.
      </>
    );
  }

  return (
    <>
      Your record is active and can be offered for work.
      {expiresOn ? ` We keep it until ${expiresOn} unless you use it before then.` : ""}
    </>
  );
}

/**
 * Art. 21 objection, for a record held on legitimate interest (P1T-171, P1T-174).
 *
 * <p><b>Honoured unconditionally</b> — there is no adjudication, no weighing of our interest
 * against theirs, and no flow in which somebody decides. Objecting deletes the record, which is why
 * it asks for the control word: the act is irreversible, and the control word is the only proof
 * this service has that the person asking is the person whose record it is. That is the same gate
 * deleting uses, because it is the same act.</p>
 */
function Objecting() {
  const erase = useEraseMyAccount();
  const [word, setWord] = useState("");
  const [open, setOpen] = useState(false);

  const object = () =>
    erase.mutate(word, {
      onSuccess: () => {
        clearSession();
        window.location.assign("/signin");
      },
    });

  return (
    <Row
      label="Objecting to us holding it"
      action={
        !open ? (
          <Button size="small" variant="outlined" color="warning" onClick={() => setOpen(true)}>
            Object
          </Button>
        ) : undefined
      }
    >
      You can object to us holding this record at all. We will not weigh your objection against our
      own interest — objecting removes your data.
      {open && (
        <Stack spacing={1} sx={{ mt: 2 }}>
          <ErrorNotice message={erase.isError ? apiErrorMessage(erase.error) : null} />
          <Typography variant="body2" color="text.primary">
            This deletes everything, permanently, and cannot be undone.
          </Typography>
          <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
            <TextField
              size="small"
              type="password"
              label="Your control word"
              value={word}
              onChange={(event) => setWord(event.target.value)}
              sx={{ maxWidth: 260 }}
            />
            <Button
              variant="outlined"
              color="warning"
              disabled={word.trim() === "" || erase.isPending}
              onClick={object}
            >
              Object and delete my record
            </Button>
            <Button onClick={() => setOpen(false)}>Keep my record</Button>
          </Stack>
        </Stack>
      )}
    </Row>
  );
}

/**
 * The foot of the page, below a rule and under its own heading, with the control word inline
 * (P1T-186). Separated from pause by <b>position and typography rather than by colour</b>, which is
 * the whole reason the page is long — and why it must not be tidied shorter.
 */
function DeleteEverything({ holdsRecord }: { holdsRecord: boolean }) {
  const erase = useEraseMyAccount();
  const [word, setWord] = useState("");

  const remove = () =>
    erase.mutate(word, {
      onSuccess: () => {
        // The session died with the account — both hosts refuse it from here on — so the only
        // honest next screen is the signed-out one.
        clearSession();
        window.location.assign("/signin");
      },
    });

  return (
    <Box sx={{ mt: 8 }}>
      <Divider sx={{ mb: 3 }} />
      <Typography variant="h6" component="h2" sx={{ mb: 1 }}>
        Deleting everything
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {holdsRecord
          ? "This removes your CV, your search index, your assessments and your sign-in. It cannot be undone, and we have no way to contact you afterwards. Proposals a Service Manager already decided on keep their decision, with your name and everything written about you removed."
          : "This removes your sign-in. There is no record under your name to remove with it. It cannot be undone, and we have no way to contact you afterwards."}
      </Typography>
      {holdsRecord && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          If you only want to stop being offered for work, <b>pause</b> further up this page
          instead — that is reversible and nothing is lost.
        </Typography>
      )}

      <ErrorNotice message={erase.isError ? apiErrorMessage(erase.error) : null} sx={{ mb: 2 }} />

      <Stack direction={{ xs: "column", sm: "row" }} spacing={2} alignItems={{ sm: "flex-start" }}>
        <TextField
          size="small"
          type="password"
          label="Your control word"
          value={word}
          onChange={(event) => setWord(event.target.value)}
          helperText="The word you chose when you signed up. It is what proves this is you."
          sx={{ maxWidth: 260 }}
        />
        <Button
          variant="outlined"
          color="error"
          disabled={word.trim() === "" || erase.isPending}
          onClick={remove}
        >
          Delete everything
        </Button>
      </Stack>
    </Box>
  );
}
