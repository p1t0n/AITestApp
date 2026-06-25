import { AppBar, Box, Button, Container, Toolbar, Typography } from "@mui/material";
import {
  Link as RouterLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useNavigate,
} from "react-router-dom";
import EmployeesPage from "./pages/EmployeesPage";
import EmployeeDetailPage from "./pages/EmployeeDetailPage";
import CvPage from "./pages/CvPage";
import CatalogPage from "./pages/CatalogPage";
import SignupPage from "./pages/SignupPage";
import SigninPage from "./pages/SigninPage";
import AgentWidget from "./components/AgentWidget";
import { signOut } from "./api";
import { useIsAuthenticated } from "./auth/useAuth";

/** Guards the protected area: renders children when signed in, else bounces to sign-in. */
function RequireAuth() {
  const authed = useIsAuthenticated();
  return authed ? <Outlet /> : <Navigate to="/signin" replace />;
}

function AuthButton() {
  const authed = useIsAuthenticated();
  const navigate = useNavigate();
  if (authed) {
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
  const authed = useIsAuthenticated();

  return (
    <Box sx={{ minHeight: "100vh", bgcolor: "grey.50" }}>
      <AppBar position="static" elevation={0}>
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            CV Manager
          </Typography>
          {authed && (
            <>
              <Button color="inherit" component={RouterLink} to="/">
                CVs
              </Button>
              <Button color="inherit" component={RouterLink} to="/catalog">
                Skill Catalog
              </Button>
            </>
          )}
          <AuthButton />
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ py: 4 }}>
        <Routes>
          {/* Public auth pages */}
          <Route path="/signin" element={<SigninPage />} />
          <Route path="/signup" element={<SignupPage />} />

          {/* Everything else requires authentication */}
          <Route element={<RequireAuth />}>
            <Route path="/" element={<EmployeesPage />} />
            <Route path="/employees/:id" element={<EmployeeDetailPage />} />
            <Route path="/employees/:id/cv" element={<CvPage />} />
            <Route path="/catalog" element={<CatalogPage />} />
          </Route>
        </Routes>
      </Container>

      {authed && <AgentWidget />}
    </Box>
  );
}
