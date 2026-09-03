import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CssBaseline, ThemeProvider } from "@mui/material";
// The three families, self-hosted and versioned like any other dependency: an authenticated
// internal tool has no business fetching a font from a third-party CDN. Each `index.css` entry
// point is the weight axis across every subset, each `@font-face` unicode-range gated, so a browser
// downloads only the ranges the page actually uses. Mono is here rather than lazy-loaded because it
// is a UI role now — Eyebrows, table headers, tags — not just `code` and `pre`.
import "@fontsource-variable/plus-jakarta-sans";
import "@fontsource-variable/dm-sans";
import "@fontsource-variable/jetbrains-mono";
import App from "./App";
import { themeFor } from "./theme";
import { useThemeMode } from "./theme/mode";
import "./index.css";

const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false } },
});

/**
 * The theme boundary. Both themes are built at module load; this only picks one, so a mode flip is
 * a `ThemeProvider` swap and not a rebuild. The Theme Mode follows the OS until a person overrides
 * it — the control that does the overriding ships with the left rail (P1T-161).
 */
function Root() {
  const mode = useThemeMode();
  return (
    <ThemeProvider theme={themeFor(mode)}>
      <CssBaseline />
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ThemeProvider>
  );
}

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <Root />
    </QueryClientProvider>
  </React.StrictMode>,
);
