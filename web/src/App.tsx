import type { ReactNode } from "react";
import { AppBar, Box, Button, Container, Toolbar, Typography } from "@mui/material";
import {
  Link as RouterLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useLocation,
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
import AppRailNav, { BRAND } from "./components/AppRail";
import { ErrorBoundary, PageErrorFallback, WidgetErrorFallback } from "./components/ErrorBoundary";
import { DOCK_PUSH_VAR, dockPushWidth, useAgentDock } from "./components/useAgentDock";
import { RAIL_PUSH_VAR, useAppRail } from "./components/useAppRail";
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

/**
 * The chrome a signed-out visitor gets. The rail is a signed-in surface — there is nothing on it to
 * navigate to yet — so the auth pages keep a slim bar with the brand and the way in. `Sign in` is a
 * frozen accessible name (`manuals/spa-design-system.md` §9), which is why it survives the rewrite.
 */
function PublicTopBar() {
  return (
    <AppBar position="sticky" elevation={0} color="default" sx={{ "@media print": { display: "none" } }}>
      <Toolbar variant="dense">
        <Typography variant="subtitle1" noWrap sx={{ flexGrow: 1 }}>
          {BRAND}
        </Typography>
        <Button color="inherit" component={RouterLink} to="/signin">
          Sign in
        </Button>
      </Toolbar>
    </AppBar>
  );
}

export default function App() {
  const authed = useIsAuthenticated();
  // Two edges, one contract. Both are `position: fixed`, both publish how much of the viewport they
  // are covering, and the root Box below pads by both — it knows neither one's state, neither one's
  // breakpoint, and neither one's width. Unset (no rail on the auth pages, no dock while signed
  // out) falls back to no padding on its own.
  const dock = useAgentDock();
  // The one place that knows about both edges, which is why the rail's squeeze rule lives here:
  // the rail gives up its labels rather than let the dock squeeze the content below its floor.
  const rail = useAppRail(dockPushWidth(dock));

  return (
    <Box
      sx={{
        minHeight: "100vh",
        bgcolor: "background.default",
        paddingLeft: `var(${RAIL_PUSH_VAR}, 0px)`,
        paddingRight: `var(${DOCK_PUSH_VAR}, 0px)`,
        transition: "padding 150ms ease",
        // Neither edge prints, so neither edge may leave a gutter on the page. Without this the
        // rail's 240px would shift every printed artifact — including a client's CV — to the right.
        "@media print": { paddingLeft: 0, paddingRight: 0 },
      }}
    >
      {authed ? <AppRailNav rail={rail} /> : <PublicTopBar />}

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
          <AgentWidget dock={dock} />
        </ErrorBoundary>
      )}
    </Box>
  );
}
