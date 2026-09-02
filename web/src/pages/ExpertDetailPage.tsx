import { useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import {
  Alert,
  Button,
  CircularProgress,
  Grid,
} from "@mui/material";
import EditIcon from "@mui/icons-material/Edit";
import DescriptionIcon from "@mui/icons-material/Description";
import DownloadIcon from "@mui/icons-material/Download";
import {
  useExpert,
  useExportExpertOnBehalf,
  useUpdateExpert,
} from "../api";
import PageHeader, { PageContainer } from "../components/PageHeader";
import ExpertRecordSections, { Section } from "../components/ExpertRecordSections";
import ExpertOwnership from "../components/ExpertOwnership";
import ExpertFormDialog from "./ExpertFormDialog";

export default function ExpertDetailPage() {
  const { id = "" } = useParams();
  const { data: e, isLoading } = useExpert(id);
  const update = useUpdateExpert(id);
  const exportOnBehalf = useExportExpertOnBehalf(id);

  const [editOpen, setEditOpen] = useState(false);

  // Early return, like the roster: the e2e capture waits for the person's name to appear before it
  // shoots, and a header rendered over an empty profile would satisfy that wait too early.
  if (isLoading || !e)
    return (
      <PageContainer width="content">
        <CircularProgress />
      </PageContainer>
    );

  return (
    <PageHeader
      title={`${e.firstName} ${e.lastName}`}
      subtitle={e.title}
      // A profile, read top to bottom in two columns — not a table. It stays capped.
      width="content"
      actions={
        <>
          <Button startIcon={<EditIcon />} onClick={() => setEditOpen(true)}>
            Edit
          </Button>
          {/* The out-of-band request (P1T-187): somebody phones in and asks for their data, since
              this service has no email to receive the request by. Taking it writes a record of the
              Service Manager who did — a fact about them, not a log of who looked at whom. */}
          <Button
            startIcon={<DownloadIcon />}
            disabled={exportOnBehalf.isPending}
            title="Download this person's data as JSON, on their behalf. The export is recorded."
            onClick={() => exportOnBehalf.mutate()}
          >
            Export their data
          </Button>
          <Button
            variant="contained"
            startIcon={<DescriptionIcon />}
            component={RouterLink}
            to={`/experts/${id}/cv`}
          >
            View CV
          </Button>
        </>
      }
    >
      {e.hiddenAt && (
        <Alert severity="info" sx={{ mb: 3 }}>
          This person paused themselves on {new Date(e.hiddenAt).toLocaleDateString()}. They are not
          offered for work — no searches, matches or scans reach them — and nothing has been
          deleted. Only they can undo it.
        </Alert>
      )}

      <Section title="Profile">
        <Grid container spacing={1}>
          <Grid item xs={6}><b>Email:</b> {e.email}</Grid>
          <Grid item xs={6}><b>Phone:</b> {e.phone ?? "—"}</Grid>
          <Grid item xs={6}><b>Location:</b> {e.location ?? "—"}</Grid>
          <Grid item xs={6}><b>Current capacity:</b> {e.currentCapacityPercent}%</Grid>
          <Grid item xs={12} sx={{ mt: 1 }}>{e.summary ?? "No summary."}</Grid>
        </Grid>
      </Section>

      <ExpertOwnership expertId={id} />

      <ExpertRecordSections expertId={id} expert={e} />

      <ExpertFormDialog
        open={editOpen}
        title="Edit expert"
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
