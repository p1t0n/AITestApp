import { AppBar, Box, Button, Container, Toolbar, Typography } from "@mui/material";
import { Link as RouterLink, Route, Routes } from "react-router-dom";
import EmployeesPage from "./pages/EmployeesPage";
import EmployeeDetailPage from "./pages/EmployeeDetailPage";
import CvPage from "./pages/CvPage";
import CatalogPage from "./pages/CatalogPage";
import SignupPage from "./pages/SignupPage";
import AgentWidget from "./components/AgentWidget";

export default function App() {
  return (
    <Box sx={{ minHeight: "100vh", bgcolor: "grey.50" }}>
      <AppBar position="static" elevation={0}>
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            CV Manager
          </Typography>
          <Button color="inherit" component={RouterLink} to="/">
            CVs
          </Button>
          <Button color="inherit" component={RouterLink} to="/catalog">
            Skill Catalog
          </Button>
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ py: 4 }}>
        <Routes>
          <Route path="/" element={<EmployeesPage />} />
          <Route path="/employees/:id" element={<EmployeeDetailPage />} />
          <Route path="/employees/:id/cv" element={<CvPage />} />
          <Route path="/catalog" element={<CatalogPage />} />
          <Route path="/signup" element={<SignupPage />} />
        </Routes>
      </Container>

      <AgentWidget />
    </Box>
  );
}
