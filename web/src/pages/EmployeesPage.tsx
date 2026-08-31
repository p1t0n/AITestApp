import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Button,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/Delete";
import DescriptionIcon from "@mui/icons-material/Description";
import { useCreateEmployee, useDeleteEmployee, useEmployees } from "../api";
import PageHeader, { PageContainer } from "../components/PageHeader";
import EmployeeFormDialog from "./EmployeeFormDialog";

function capacityColor(pct: number): "success" | "warning" | "default" {
  if (pct >= 100) return "success";
  if (pct > 0) return "warning";
  return "default";
}

export default function EmployeesPage() {
  const { data, isLoading } = useEmployees();
  const create = useCreateEmployee();
  const del = useDeleteEmployee();
  const navigate = useNavigate();
  const [dialogOpen, setDialogOpen] = useState(false);

  // Deliberately still an early return rather than a spinner *under* the header: the e2e capture
  // waits for `New CV` to decide the roster has arrived, and a header that renders while the table
  // is empty would hand it a screenshot of a spinner (`manuals/spa-design-system.md` §10).
  if (isLoading)
    return (
      <PageContainer width="wide">
        <CircularProgress />
      </PageContainer>
    );

  return (
    <PageHeader
      title="CVs"
      // The roster is nine columns wide at its widest and reads better the more of them fit.
      width="wide"
      actions={
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialogOpen(true)}>
          New CV
        </Button>
      }
    >
      <Paper>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Title</TableCell>
              <TableCell>Location</TableCell>
              <TableCell>Availability (today)</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data?.map((e) => (
              <TableRow
                key={e.id}
                hover
                sx={{ cursor: "pointer" }}
                onClick={() => navigate(`/employees/${e.id}`)}
              >
                <TableCell>
                  {e.firstName} {e.lastName}
                </TableCell>
                <TableCell>{e.title}</TableCell>
                <TableCell>{e.location ?? "—"}</TableCell>
                <TableCell>
                  <Chip
                    label={`${e.currentCapacityPercent}%`}
                    color={capacityColor(e.currentCapacityPercent)}
                  />
                </TableCell>
                <TableCell align="right" onClick={(ev) => ev.stopPropagation()}>
                  <IconButton title="View CV" onClick={() => navigate(`/employees/${e.id}/cv`)}>
                    <DescriptionIcon />
                  </IconButton>
                  <IconButton
                    title="Delete"
                    color="error"
                    onClick={() => {
                      if (confirm(`Delete ${e.firstName} ${e.lastName}?`)) del.mutate(e.id);
                    }}
                  >
                    <DeleteIcon />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>

      <EmployeeFormDialog
        open={dialogOpen}
        title="New employee"
        onClose={() => setDialogOpen(false)}
        onSave={(dto) => create.mutateAsync(dto)}
      />
    </PageHeader>
  );
}
