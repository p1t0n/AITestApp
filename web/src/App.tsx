import type { ReactNode } from "react";
import { AppBar, Box, Button, Toolbar, Typography } from "@mui/material";
import {
  Link as RouterLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useLocation,
} from "react-router-dom";
import ExpertsPage from "./pages/ExpertsPage";
import ExpertDetailPage from "./pages/ExpertDetailPage";
import CvPage from "./pages/CvPage";
import CatalogPage from "./pages/CatalogPage";
import SignupPage from "./pages/SignupPage";
import SigninPage from "./pages/SigninPage";
import RecoverPage from "./pages/RecoverPage";
import UsersPage from "./pages/UsersPage";
import MyWorkspacePage from "./pages/MyWorkspacePage";
import AgentWidget from "./components/AgentWidget";
import AppRailNav, { BRAND } from "./components/AppRail";
import CommandPalette from "./components/CommandPalette";
import { ErrorBoundary, PageErrorFallback, WidgetErrorFallback } from "./components/ErrorBoundary";
import { PageContainer } from "./components/PageHeader";
import { DOCK_PUSH_VAR, dockPushWidth, useAgentDock } from "./components/useAgentDock";
import { RAIL_PUSH_VAR, useAppRail } from "./components/useAppRail";
import { useIsAuthenticated, useSessionRole } from "./auth/useAuth";
import { landingFor, type SessionRole } from "./auth/roles";

/**
 * Guards a routed area, and says who it is for (P1T-181). Every protected route now declares its
 * audience, mirroring the server, where the audience is declared per endpoint and the fallback is
 * staff-only.
 *
 * Three outcomes, and the third is the one that matters: a signed-in user who asks for a route
 * their role cannot have goes to **their own landing page**, never to `/signin`. Sending them to
 * the gate would claim they are signed out — they are not — and offer them a sign-in they have no
 * second account for.
 *
 * A session with no stored role predates the split; its token carries neither claim the server now
 * requires, so the gate is the honest destination.
 */
function RequireAuth({ role }: { role: SessionRole }) {
  const authed = useIsAuthenticated();
  const actual = useSessionRole();

  if (!authed || actual === null) {
    return <Navigate to="/signin" replace />;
  }

  return actual === role ? <Outlet /> : <Navigate to={landingFor(actual)} replace />;
}

/**
 * The routed area under its own error boundary: a render throw inside a page shows a fallback with
 * a way back instead of a white screen. Keyed by the path, so navigating away clears the error and
 * the next route renders normally.
 *
 * The fallback carries its own `PageContainer`, because it renders *instead of* a page — and since
 * P1T-162 the page is what owns the width. Without it a failed catalog would put its `Paper` flush
 * against the rail.
 */
function RoutedArea({ children }: { children: ReactNode }) {
  const location = useLocation();
  return (
    <ErrorBoundary
      resetKey={location.pathname}
      fallback={(error, reset) => (
        <PageContainer width="content">
          <PageErrorFallback error={error} reset={reset} />
        </PageContainer>
      )}
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

      {/* No container here any more. Width is a per-page decision now — the roster wants every
          column it can get, a sign-in form wants 440px — and `maxWidth="lg"` centred inside
          whatever the two edges leave over could not be right for both (P1T-162,
          `manuals/spa-design-system.md` §5). Each page states its own width through `PageHeader`. */}
      <RoutedArea>
        <Routes>
          {/* Public auth pages */}
          <Route path="/signin" element={<SigninPage />} />
          <Route path="/signup" element={<SignupPage />} />
          <Route path="/recover" element={<RecoverPage />} />

          {/* Staff surfaces. The roster, the catalog and user administration are all staffing
              data — an Expert reaching any of them would be reading other people's CVs. */}
          <Route element={<RequireAuth role="ServiceManager" />}>
            <Route path="/" element={<ExpertsPage />} />
            <Route path="/experts/:id" element={<ExpertDetailPage />} />
            <Route path="/experts/:id/cv" element={<CvPage />} />
            <Route path="/catalog" element={<CatalogPage />} />
            <Route path="/users" element={<UsersPage />} />
          </Route>

          {/* The Expert's own space. Thin on purpose — P1T-190 builds the workspace; this slice
              needs a landing page that exists, so a wrong-role redirect has somewhere to go. */}
          <Route element={<RequireAuth role="Expert" />}>
            <Route path="/me" element={<MyWorkspacePage />} />
          </Route>
        </Routes>
      </RoutedArea>

      {/* The widget sits under its own boundary: the assistant crashing must not take the roster
          with it. Panels have a second, inner boundary inside the widget. */}
      {authed && (
        <>
          {/* ⌘K (P1T-165). Mounted here rather than in the rail, which only carries the visible
              trigger: the palette has to open with no rail on screen — below `md` the rail is a
              closed drawer — and it acts on the dock as well as on the routes, so it belongs
              beside both. It takes the dock because "jump to an agent surface" opens it. */}
          <CommandPalette dock={dock} />
          <ErrorBoundary fallback={(error, reset) => <WidgetErrorFallback error={error} reset={reset} />}>
            <AgentWidget dock={dock} />
          </ErrorBoundary>
        </>
      )}
    </Box>
  );
}
