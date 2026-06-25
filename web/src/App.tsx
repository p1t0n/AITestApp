import { AppBar, Box, Button, Container, Toolbar, Typography } from "@mui/material";
import { Link as RouterLink, Route, Routes, useNavigate } from "react-router-dom";
import EmployeesPage from "./pages/EmployeesPage";
import EmployeeDetailPage from "./pages/EmployeeDetailPage";
import CvPage from "./pages/CvPage";
import CatalogPage from "./pages/CatalogPage";
import SignupPage from "./pages/SignupPage";
import SigninPage from "./pages/SigninPage";
import AgentWidget from "./components/AgentWidget";
import { isSignedIn, signOut } from "./api";

function AuthButton() {
  const navigate = useNavigate();
  // localStorage isn't reactive; this reflects the token at render time. P1T-22 (app-wide gate)
  // introduces a proper auth context that updates live.
  if (isSignedIn()) {
    return (
      <Button
        color="inherit"
        onClick={() => {
          signOut();
          navigate("/signin");
        }}
      >
        Sign out
      </Button>
    );
  }
  return (
    <Button color="inherit" component={RouterLink} to="/signin">
      Sign in
    </Button>
  );
}

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
          <AuthButton />
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ py: 4 }}>
        <Routes>
          <Route path="/" element={<EmployeesPage />} />
          <Route path="/employees/:id" element={<EmployeeDetailPage />} />
          <Route path="/employees/:id/cv" element={<CvPage />} />
          <Route path="/catalog" element={<CatalogPage />} />
          <Route path="/signup" element={<SignupPage />} />
          <Route path="/signin" element={<SigninPage />} />
        </Routes>
      </Container>

      <AgentWidget />
    </Box>
  );
}
