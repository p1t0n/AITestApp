/**
 * PROTOTYPE — throwaway. The floating variant switcher.
 *
 * Deliberately loud and unlike the rest of the app, so nobody mistakes it for the design being
 * judged. Hidden in production builds: a stray merge cannot ship it.
 */
import { useEffect } from "react";
import { Box, Chip, IconButton, Stack, Typography } from "@mui/material";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeft";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import { PROTOTYPES_ENABLED } from "../pages/prototype/privacyState";

export interface PrototypeSwitcherProps {
  variants: string[];
  names: Record<string, string>;
  current: string;
  onChange: (v: string) => void;
  /** Rendered above the bar — the live state readout, so every action's effect is visible. */
  readout?: React.ReactNode;
  /** State-flipping controls, so the reviewer can drive the surface into each case. */
  controls?: React.ReactNode;
}

export default function PrototypeSwitcher({
  variants,
  names,
  current,
  onChange,
  readout,
  controls,
}: PrototypeSwitcherProps) {
  const i = Math.max(0, variants.indexOf(current));
  const go = (delta: number) =>
    onChange(variants[(i + delta + variants.length) % variants.length]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = document.activeElement;
      const typing =
        el instanceof HTMLInputElement ||
        el instanceof HTMLTextAreaElement ||
        (el instanceof HTMLElement && el.isContentEditable);
      if (typing) return;
      if (e.key === "ArrowLeft") go(-1);
      if (e.key === "ArrowRight") go(1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  // After the hooks, never before: a conditional hook is a lint error and a real bug.
  if (!PROTOTYPES_ENABLED) return null;

  return (
    <Box
      sx={{
        position: "fixed",
        bottom: 16,
        left: "50%",
        transform: "translateX(-50%)",
        zIndex: 2000,
        maxWidth: "min(920px, calc(100vw - 32px))",
      }}
    >
      <Stack spacing={1} alignItems="center">
        {controls && (
          <Box
            sx={{
              bgcolor: "#111",
              color: "#fff",
              borderRadius: 2,
              px: 1.5,
              py: 1,
              border: "2px solid #ff4081",
            }}
          >
            {controls}
          </Box>
        )}

        {readout && (
          <Box
            sx={{
              bgcolor: "#111",
              color: "#9fe",
              borderRadius: 2,
              px: 1.5,
              py: 0.75,
              fontFamily: "ui-monospace, monospace",
              fontSize: 11,
              border: "2px solid #ff4081",
            }}
          >
            {readout}
          </Box>
        )}

        <Stack
          direction="row"
          alignItems="center"
          spacing={1}
          sx={{
            bgcolor: "#ff4081",
            color: "#fff",
            borderRadius: 999,
            pl: 0.5,
            pr: 0.5,
            boxShadow: 6,
          }}
        >
          <IconButton size="small" onClick={() => go(-1)} sx={{ color: "#fff" }} aria-label="Previous variant">
            <ChevronLeftIcon fontSize="small" />
          </IconButton>
          <Chip
            size="small"
            label="PROTOTYPE"
            sx={{ bgcolor: "#fff", color: "#ff4081", fontWeight: 700, height: 20 }}
          />
          <Typography variant="body2" sx={{ fontWeight: 600, whiteSpace: "nowrap", px: 0.5 }}>
            {current} — {names[current]}
          </Typography>
          <IconButton size="small" onClick={() => go(1)} sx={{ color: "#fff" }} aria-label="Next variant">
            <ChevronRightIcon fontSize="small" />
          </IconButton>
        </Stack>
      </Stack>
    </Box>
  );
}
