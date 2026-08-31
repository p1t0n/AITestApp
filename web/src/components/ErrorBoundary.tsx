import { Component } from "react";
import type { ErrorInfo, ReactNode } from "react";
import { Box, Button, Paper, Stack, Typography } from "@mui/material";
import { useNavigate } from "react-router-dom";

interface Props {
  children: ReactNode;
  /** Rendered in place of the children after a throw. Gets the error and a reset that retries. */
  fallback: (error: Error, reset: () => void) => ReactNode;
  /** When this value changes, a caught error is cleared — e.g. the route path, or the dock mode. */
  resetKey?: unknown;
}

interface State {
  error: Error | null;
}

/**
 * Catches render-time throws below it so one broken subtree does not take the page to white
 * (P1T-153). A boundary only catches *rendering* errors — async failures still flow through each
 * component's own error state and {@link ErrorNotice}; this is the net under the case nobody
 * handled.
 *
 * Recovery is two-sided: `reset()` retries in place, and changing `resetKey` clears the error on
 * its own, so navigating to another route (or switching dock panel) is itself the way back.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // No error-reporting backend in this POC; the console is the record.
    console.error("Unhandled render error", error, info.componentStack);
  }

  componentDidUpdate(prev: Props) {
    if (this.state.error && prev.resetKey !== this.props.resetKey) {
      this.setState({ error: null });
    }
  }

  reset = () => this.setState({ error: null });

  render() {
    const { error } = this.state;
    return error ? this.props.fallback(error, this.reset) : this.props.children;
  }
}

/**
 * Fallback for the routed area: says what broke and offers the roster as a way back. Resetting and
 * navigating happen together so the button works even when the crashed route *is* the roster.
 */
export function PageErrorFallback({ error, reset }: { error: Error; reset: () => void }) {
  const navigate = useNavigate();
  return (
    <Paper sx={{ p: 3 }} role="alert">
      <Stack spacing={2} alignItems="flex-start">
        <Typography variant="h6">This page stopped working</Typography>
        <Typography variant="body2" color="text.secondary">
          {error.message}
        </Typography>
        <Button
          variant="contained"
          onClick={() => {
            reset();
            navigate("/");
          }}
        >
          Back to CVs
        </Button>
      </Stack>
    </Paper>
  );
}

/**
 * Fallback for an agent panel. Stays inside the dock — the header and the panel switcher above it
 * are still live, so picking another agent is both the escape and the retry.
 */
export function DockErrorFallback({ error, reset }: { error: Error; reset: () => void }) {
  return (
    <Box sx={{ p: 2 }} role="alert">
      <Stack spacing={1.5} alignItems="flex-start">
        <Typography variant="subtitle2">This panel stopped working</Typography>
        <Typography variant="body2" color="text.secondary">
          {error.message}
        </Typography>
        <Button variant="outlined" onClick={reset}>
          Try again
        </Button>
      </Stack>
    </Box>
  );
}

/**
 * Fallback for the widget chrome itself. The roster underneath is untouched, so this only has to
 * say the assistant is gone and offer it back.
 */
export function WidgetErrorFallback({ reset }: { error: Error; reset: () => void }) {
  return (
    <Paper
      variant="elevation"
      elevation={8}
      role="alert"
      sx={{ position: "fixed", bottom: 24, right: 24, zIndex: 1300, p: 2, maxWidth: 320 }}
    >
      <Stack spacing={1} alignItems="flex-start">
        <Typography variant="body2">The agents assistant stopped working.</Typography>
        <Button onClick={reset}>
          Reload it
        </Button>
      </Stack>
    </Paper>
  );
}

export default ErrorBoundary;
