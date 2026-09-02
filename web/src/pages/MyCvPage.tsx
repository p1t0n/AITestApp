import { useState } from "react";
import { Alert, Button, Chip, CircularProgress, Grid, Stack, Typography } from "@mui/material";
import EditIcon from "@mui/icons-material/Edit";
import { Navigate } from "react-router-dom";
import { apiErrorMessage, useExpert, useMyVisibility, useUpdateExpert } from "../api";
import PageHeader, { PageContainer } from "../components/PageHeader";
import ExpertRecordSections, { Section } from "../components/ExpertRecordSections";
import { ErrorNotice } from "../components/ErrorNotice";
import ExpiryBanner from "../components/ExpiryBanner";
import ExpertFormDialog from "./ExpertFormDialog";

/**
 * The Expert's own record, and where they land (P1T-190). It is what they came to do — a dashboard
 * reporting three mostly-static facts is a page that tells you nothing on most visits, so the
 * profile status is a compact strip on the editor instead.
 *
 * <p>A thin shell around <c>ExpertRecordSections</c>, which the Service Manager's page uses too.
 * The alternative — one page serving both roles with controls hidden by role — was rejected because
 * "hidden for Experts" is one prop away from not hidden. What differs here is what the page
 * <em>offers</em>: no ownership card, no on-behalf export, no delete, and the email field locked.</p>
 *
 * <p>Somebody who owns no record is sent to the claim-status page: there is nothing to edit, and an
 * empty editor would misrepresent what is happening to them.</p>
 */
export default function MyCvPage() {
  const mine = useMyVisibility();
  const [editOpen, setEditOpen] = useState(false);

  // Which record is mine, and is it paused — the one read that answers both, and the same call the
  // pause control uses. A 404 is the legitimate "you own none" state (P1T-182), not an error.
  if (mine.isLoading) {
    return (
      <PageContainer width="content">
        <CircularProgress />
      </PageContainer>
    );
  }

  if (mine.isError || !mine.data) {
    return <Navigate to="/me/claim" replace />;
  }

  return <Editor expertId={mine.data.expertId} paused={mine.data.hidden} editOpen={editOpen} setEditOpen={setEditOpen} />;
}

function Editor({
  expertId,
  paused,
  editOpen,
  setEditOpen,
}: {
  expertId: string;
  paused: boolean;
  editOpen: boolean;
  setEditOpen: (open: boolean) => void;
}) {
  const { data: e, isLoading, isError, error } = useExpert(expertId);
  const update = useUpdateExpert(expertId);

  if (isLoading || !e) {
    return (
      <PageContainer width="content">
        {isError ? <ErrorNotice message={apiErrorMessage(error)} /> : <CircularProgress />}
      </PageContainer>
    );
  }

  return (
    <PageHeader
      title="My CV"
      subtitle={`${e.firstName} ${e.lastName}`.trim() || undefined}
      width="content"
      actions={
        <Button startIcon={<EditIcon />} onClick={() => setEditOpen(true)}>
          Edit details
        </Button>
      }
    >
      {/* The one thing here with a deadline (P1T-188). It lives on this page rather than on
          Privacy & data, where the state is stated in prose: two surfaces saying the same thing is
          exactly what the prototype run rejected, and this is the page somebody actually spends
          time on. Reading it is activity, so it has already pushed the date back. */}
      <ExpiryBanner />

      {/* The status strip: compact, on the editor, and only saying something when there is
          something to say. How the five states should read at a glance is P1T-175's open visual
          question — this is the structure, not the answer to it. */}
      {paused && (
        <Alert severity="info" sx={{ mb: 3 }}>
          You are paused, so you are not being offered for work. Nothing has been deleted, and you
          can start again from Privacy &amp; data whenever you like.
        </Alert>
      )}

      <Section title="Your details">
        <Grid container spacing={1}>
          <Grid item xs={6}><b>Email:</b> {e.email}</Grid>
          <Grid item xs={6}><b>Phone:</b> {e.phone ?? "—"}</Grid>
          <Grid item xs={6}><b>Location:</b> {e.location ?? "—"}</Grid>
          <Grid item xs={6}>
            <Stack direction="row" spacing={1} alignItems="center">
              <b>Availability today:</b>
              <Chip size="small" label={`${e.currentCapacityPercent}%`} />
            </Stack>
          </Grid>
          <Grid item xs={12} sx={{ mt: 1 }}>
            {e.summary ?? <Typography color="text.secondary">No summary yet.</Typography>}
          </Grid>
        </Grid>
      </Section>

      <ExpertRecordSections expertId={expertId} expert={e} />

      <ExpertFormDialog
        open={editOpen}
        title="Edit your details"
        emailLocked
        initial={{
          firstName: e.firstName,
          lastName: e.lastName,
          title: e.title,
          email: e.email,
          phone: e.phone,
          location: e.location,
          summary: e.summary,
          photoUrl: e.photoUrl,
        }}
        onClose={() => setEditOpen(false)}
        onSave={(dto) => update.mutateAsync(dto)}
      />
    </PageHeader>
  );
}
