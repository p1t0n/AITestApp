import type { ReactNode } from "react";
import { AppBar, Box, Button, Container, Toolbar, Typography, useMediaQuery } from "@mui/material";
import {
  Link as RouterLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useLocation,
  useNavigate,
} from "react-router-dom";
import EmployeesPage from "./pages/EmployeesPage";
import EmployeeDetailPage from "./pages/EmployeeDetailPage";
import CvPage from "./pages/CvPage";
import CatalogPage from "./pages/CatalogPage";
import SignupPage from "./pages/SignupPage";
import SigninPage from "./pages/SigninPage";
import RecoverPage from "./pages/RecoverPage";
import UsersPage from "./pages/UsersPage";
import AgentWidget from "./components/AgentWidget";
import { ErrorBoundary, PageErrorFallback, WidgetErrorFallback } from "./components/ErrorBoundary";
import { useAgentDock } from "./components/useAgentDock";
import { signOut } from "./api";
import { useIsAuthenticated } from "./auth/useAuth";

/** Guards the protected area: renders children when signed in, else bounces to sign-in. */
function RequireAuth() {
  const authed = useIsAuthenticated();
  return authed ? <Outlet /> : <Navigate to="/signin" replace />;
}

/**
 * The routed area under its own error boundary: a render throw inside a page shows a fallback with
 * a way back instead of a white screen. Keyed by the path, so navigating away clears the error and
 * the next route renders normally.
 */
function RoutedArea({ children }: { children: ReactNode }) {
  const location = useLocation();
  return (
    <ErrorBoundary
      resetKey={location.pathname}
      fallback={(error, reset) => <PageErrorFallback error={error} reset={reset} />}
    >
      {children}
    </ErrorBoundary>
  );
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
  const dock = useAgentDock();
  const isNarrow = useMediaQuery("(max-width:600px)");

  // A docked sidebar pushes the whole app left (full-width overlay on narrow screens doesn't push).
  const pushContent = authed && dock.open && dock.docked && !isNarrow;

  return (
    <Box
      sx={{
        minHeight: "100vh",
        bgcolor: "grey.50",
        paddingRight: pushContent ? `${dock.width}px` : 0,
        transition: "padding-right 150ms ease",
      }}
    >
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
              <Button color="inherit" component={RouterLink} to="/users">
                Users
              </Button>
            </>
          )}
          <AuthButton />
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ py: 4 }}>
        <RoutedArea>
          <Routes>
            {/* Public auth pages */}
            <Route path="/signin" element={<SigninPage />} />
            <Route path="/signup" element={<SignupPage />} />
            <Route path="/recover" element={<RecoverPage />} />

            {/* Everything else requires authentication */}
            <Route element={<RequireAuth />}>
              <Route path="/" element={<EmployeesPage />} />
              <Route path="/employees/:id" element={<EmployeeDetailPage />} />
              <Route path="/employees/:id/cv" element={<CvPage />} />
              <Route path="/catalog" element={<CatalogPage />} />
              <Route path="/users" element={<UsersPage />} />
            </Route>
          </Routes>
        </RoutedArea>
      </Container>

      {/* The widget sits under its own boundary: the assistant crashing must not take the roster
          with it. Panels have a second, inner boundary inside the widget. */}
      {authed && (
        <ErrorBoundary fallback={(error, reset) => <WidgetErrorFallback error={error} reset={reset} />}>
          <AgentWidget dock={dock} isNarrow={isNarrow} />
        </ErrorBoundary>
      )}
    </Box>
  );
}
