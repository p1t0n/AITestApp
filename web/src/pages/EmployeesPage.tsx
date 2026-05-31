import { useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/Delete";
import DescriptionIcon from "@mui/icons-material/Description";
import { useCreateEmployee, useDeleteEmployee, useEmployees } from "../api";
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

  if (isLoading) return <CircularProgress />;

  return (
    <Box>
      <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4">Employees</Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialogOpen(true)}>
          New employee
        </Button>
      </Stack>

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
                    size="small"
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
    </Box>
  );
}
