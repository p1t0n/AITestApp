import { useState } from "react";
import { Box, Fab, IconButton, Paper, Stack, Tab, Tabs, Tooltip, Typography } from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import CloseIcon from "@mui/icons-material/Close";
import OpenInFullIcon from "@mui/icons-material/OpenInFull";
import CloseFullscreenIcon from "@mui/icons-material/CloseFullscreen";
import type { AgentDock } from "./useAgentDock";
import type { AgentJobRequest } from "../api";
import { RosterChat } from "./agent/RosterQaTab";
import { AgentJobForm } from "./agent/AgentJobTab";
import { ShortlistPanel } from "./agent/ShortlistTab";
import { StaffingPanel } from "./agent/StaffingTab";
import { IngestionPanel } from "./agent/IngestionTab";
import { UsagePanel } from "./agent/UsageTab";

type Mode = "roster" | "cv-tailoring" | "match" | "shortlist" | "staffing" | "ingestion" | "usage";

const TABS: { mode: Mode; label: string }[] = [
  { mode: "roster", label: "Roster Q&A" },
  { mode: "cv-tailoring", label: "Tailor CV" },
  { mode: "match", label: "Match" },
  { mode: "shortlist", label: "Shortlist" },
  { mode: "staffing", label: "Staffing" },
  { mode: "ingestion", label: "Ingest" },
  { mode: "usage", label: "Usage" },
];

export default function AgentWidget({ dock, isNarrow }: { dock: AgentDock; isNarrow: boolean }) {
  const [mode, setMode] = useState<Mode>("roster");

  // Cross-tab drill-ins ("Run full Match" on a shortlist card, "Open in Match" / "Tailor CV" on a
  // staffing card) jump to the target tab with the employee + JD pre-filled. Cleared on any manual
  // tab click so a stale prefill never resurfaces later.
  const [prefill, setPrefill] = useState<{
    mode: "cv-tailoring" | "match";
    request: AgentJobRequest;
  } | null>(null);
  function openPrefilled(target: "cv-tailoring" | "match", employeeId: string, jobDescription: string) {
    setPrefill({ mode: target, request: { employeeId, jobDescription } });
    setMode(target);
  }

  // Drag the left edge of the docked sidebar to resize. Width is viewport-minus-cursor, clamped by
  // the hook. Disabled on narrow screens (full-width overlay, no resize).
  const startResize = (e: React.MouseEvent) => {
    e.preventDefault();
    const onMove = (ev: MouseEvent) => dock.setWidth(window.innerWidth - ev.clientX);
    const onUp = () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
      document.body.style.userSelect = "";
    };
    document.body.style.userSelect = "none";
    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
  };

  const dockedWide = dock.docked && !isNarrow;
  const dockedNarrow = dock.docked && isNarrow;

  const panelSx = !dock.docked
    ? {
        bottom: 96,
        right: 24,
        width: 460,
        maxWidth: "calc(100vw - 48px)",
        height: 620,
        maxHeight: "calc(100vh - 140px)",
        borderRadius: 3,
      }
    : dockedNarrow
      ? { inset: 0, width: "100vw", height: "100vh", borderRadius: 0 }
      : { top: 0, right: 0, width: dock.width, height: "100vh", borderRadius: 0, borderLeft: 1, borderColor: "divider" };

  return (
    <>
      {!dock.open && (
        <Fab
          color="primary"
          aria-label="Open the agents assistant"
          onClick={dock.toggleOpen}
          sx={{ position: "fixed", bottom: 24, right: 24, zIndex: 1300 }}
        >
          <SmartToyIcon />
        </Fab>
      )}

      {dock.open && (
        <Paper
          elevation={dockedWide ? 4 : 8}
          square={dock.docked}
          sx={{
            position: "fixed",
            display: "flex",
            flexDirection: "column",
            zIndex: 1300,
            overflow: "hidden",
            ...panelSx,
          }}
        >
          {dockedWide && (
            <Box
              onMouseDown={startResize}
              sx={{
                position: "absolute",
                left: 0,
                top: 0,
                bottom: 0,
                width: 6,
                cursor: "col-resize",
                zIndex: 1,
                "&:hover": { bgcolor: "primary.light" },
              }}
            />
          )}

          <Box sx={{ px: 2, py: 1.5, bgcolor: "primary.main", color: "primary.contrastText" }}>
            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Stack direction="row" alignItems="center" spacing={1}>
                <SmartToyIcon fontSize="small" />
                <Typography variant="subtitle1">Agents</Typography>
              </Stack>
              <Stack direction="row" alignItems="center">
                <Tooltip title={dock.docked ? "Float" : "Dock to side"}>
                  <IconButton
                    size="small"
                    onClick={() => dock.setDocked(!dock.docked)}
                    sx={{ color: "inherit" }}
                  >
                    {dock.docked ? (
                      <CloseFullscreenIcon fontSize="small" />
                    ) : (
                      <OpenInFullIcon fontSize="small" />
                    )}
                  </IconButton>
                </Tooltip>
                <IconButton size="small" onClick={dock.close} sx={{ color: "inherit" }}>
                  <CloseIcon fontSize="small" />
                </IconButton>
              </Stack>
            </Stack>
          </Box>

          <Tabs
            value={mode}
            onChange={(_, v: Mode) => {
              setPrefill(null);
              setMode(v);
            }}
            variant="fullWidth"
            sx={{ minHeight: 40, borderBottom: 1, borderColor: "divider" }}
          >
            {TABS.map((t) => (
              <Tab key={t.mode} value={t.mode} label={t.label} sx={{ minHeight: 40, py: 0 }} />
            ))}
          </Tabs>

          {/* Remount per mode so each keeps its own independent state. The job forms also
              remount per prefill so a new drill-in always lands its values. */}
          {mode === "roster" ? (
            <RosterChat key="roster" />
          ) : mode === "usage" ? (
            <UsagePanel key="usage" />
          ) : mode === "ingestion" ? (
            <IngestionPanel key="ingestion" />
          ) : mode === "shortlist" ? (
            <ShortlistPanel
              key="shortlist"
              onRunMatch={(employeeId, jd) => openPrefilled("match", employeeId, jd)}
            />
          ) : mode === "staffing" ? (
            <StaffingPanel
              key="staffing"
              onOpenInMatch={(employeeId, jd) => openPrefilled("match", employeeId, jd)}
              onTailorCv={(employeeId, jd) => openPrefilled("cv-tailoring", employeeId, jd)}
            />
          ) : (
            <AgentJobForm
              key={prefill?.mode === mode ? `${mode}-${prefill.request.employeeId}` : mode}
              mode={mode}
              initial={prefill?.mode === mode ? prefill.request : undefined}
            />
          )}
        </Paper>
      )}
    </>
  );
}
