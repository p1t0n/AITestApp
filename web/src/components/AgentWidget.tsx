import { useState } from "react";
import {
  Box,
  Button,
  Fab,
  IconButton,
  ListSubheader,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Tooltip,
  Typography,
} from "@mui/material";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import CloseIcon from "@mui/icons-material/Close";
import OpenInFullIcon from "@mui/icons-material/OpenInFull";
import CloseFullscreenIcon from "@mui/icons-material/CloseFullscreen";
import DataUsageIcon from "@mui/icons-material/DataUsage";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import { useDockPush, type AgentDock } from "./useAgentDock";
import { ErrorBoundary, DockErrorFallback } from "./ErrorBoundary";
import type { AgentJobRequest } from "../api";
import { RosterChat } from "./agent/RosterQaTab";
import { AgentJobForm } from "./agent/AgentJobTab";
import { ShortlistPanel } from "./agent/ShortlistTab";
import { StaffingPanel } from "./agent/StaffingTab";
import { RosterScanPanel } from "./agent/RosterScanTab";
import { BenchPanel } from "./agent/BenchTab";
import { IngestionPanel } from "./agent/IngestionTab";
import { UsagePanel } from "./agent/UsageTab";

/** One agent surface. Usage is deliberately absent — it is the token ledger, not an agent. */
type Surface =
  | "roster"
  | "cv-tailoring"
  | "match"
  | "interview-kit"
  | "shortlist"
  | "staffing"
  | "roster-scan"
  | "bench"
  | "ingestion";

// The dock grew one tab per agent and the flat strip stopped fitting five agents ago: `fullWidth`
// tabs divide DOCK_MIN_WIDTH (360px) by the tab count, so ten of them left ~36px a label and the
// labels were abbreviated to survive it. A grouped picker is O(1) in panel width instead of O(n)
// in agent count, which is what has to hold when the eleventh agent lands — it joins a group and
// nothing else moves. The groups are the categories the flat list was hiding: these were never
// ten peers.
const SURFACE_GROUPS: { category: string; surfaces: { surface: Surface; label: string }[] }[] = [
  {
    category: "Ask about the roster",
    surfaces: [{ surface: "roster", label: "Roster Q&A" }],
  },
  {
    category: "Act on one person",
    surfaces: [
      { surface: "cv-tailoring", label: "Tailor CV" },
      { surface: "match", label: "Match" },
      { surface: "interview-kit", label: "Interview kit" },
    ],
  },
  {
    category: "Act on a role",
    surfaces: [
      { surface: "shortlist", label: "Shortlist" },
      { surface: "staffing", label: "Staffing" },
      { surface: "roster-scan", label: "Roster scan" },
      { surface: "bench", label: "Bench report" },
    ],
  },
  {
    category: "Operate",
    surfaces: [{ surface: "ingestion", label: "Resume ingest" }],
  },
];

const SURFACE_LABELS: Record<Surface, string> = Object.fromEntries(
  SURFACE_GROUPS.flatMap((g) => g.surfaces.map((s) => [s.surface, s.label])),
) as Record<Surface, string>;

/** Prefix of the picker's accessible name (`"Agent surface: Match"`) — also how the specs reach
 * it. The visible text stays the bare surface label, so the accessible name still contains it. */
export const SURFACE_PICKER_LABEL = "Agent surface";

export default function AgentWidget({ dock }: { dock: AgentDock }) {
  const [surface, setSurface] = useState<Surface>("roster");
  const [pickerAnchor, setPickerAnchor] = useState<HTMLElement | null>(null);

  // The dock is fixed-position, so it tells the page how much of it is covered rather than taking
  // part in layout. Nobody upstream needs to know the width, the surface, or the breakpoint.
  useDockPush(dock);

  // The token ledger is status, not a surface: it sits in the panel header next to the dock and
  // close controls, and peeking at it never costs a place in the agent picker.
  const [usageOpen, setUsageOpen] = useState(false);

  // Cross-surface drill-ins ("Run full Match" on a shortlist card, "Open in Match" / "Tailor CV" on
  // a staffing card) jump to the target surface with the employee + JD pre-filled. Cleared on any
  // manual navigation — picking a surface, or opening the ledger — so a stale prefill never
  // resurfaces later.
  const [prefill, setPrefill] = useState<{
    mode: "cv-tailoring" | "match";
    request: AgentJobRequest;
  } | null>(null);
  function openPrefilled(target: "cv-tailoring" | "match", employeeId: string, jobDescription: string) {
    setPrefill({ mode: target, request: { employeeId, jobDescription } });
    setUsageOpen(false);
    setSurface(target);
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

  const dockedWide = dock.docked && !dock.isNarrow;
  const dockedNarrow = dock.docked && dock.isNarrow;

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
                <Tooltip title="Token usage">
                  <IconButton
                    size="small"
                    aria-label="Token usage"
                    aria-pressed={usageOpen}
                    onClick={() => {
                      setPrefill(null);
                      setUsageOpen((o) => !o);
                    }}
                    sx={{ color: "inherit" }}
                  >
                    <DataUsageIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
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

          {/* Navigation chrome first, and OUTSIDE the boundary below: whatever a panel does, the
              way out of it has to keep rendering. */}
          {usageOpen ? (
            <Box
              sx={{
                px: 1,
                py: 0.75,
                borderBottom: 1,
                borderColor: "divider",
                display: "flex",
                alignItems: "center",
                gap: 1,
              }}
            >
              <Button size="small" startIcon={<ArrowBackIcon />} onClick={() => setUsageOpen(false)}>
                Back to {SURFACE_LABELS[surface]}
              </Button>
              <Typography variant="body2" color="text.secondary">
                Token usage
              </Typography>
            </Box>
          ) : (
            <Box sx={{ px: 1, py: 0.75, borderBottom: 1, borderColor: "divider" }}>
              {/* One control, one label — readable at any dock width, including DOCK_MIN_WIDTH.
                  A Menu rather than a Select: Select clones `role="option"` onto every child,
                  which would announce the four group headers as pickable surfaces. */}
              <Button
                fullWidth
                color="inherit"
                onClick={(e) => setPickerAnchor(e.currentTarget)}
                aria-haspopup="menu"
                aria-expanded={pickerAnchor ? true : undefined}
                aria-label={`${SURFACE_PICKER_LABEL}: ${SURFACE_LABELS[surface]}`}
                endIcon={<ArrowDropDownIcon />}
                sx={{ justifyContent: "space-between", textTransform: "none", px: 1.5 }}
              >
                {SURFACE_LABELS[surface]}
              </Button>
              <Menu
                anchorEl={pickerAnchor}
                open={!!pickerAnchor}
                onClose={() => setPickerAnchor(null)}
                MenuListProps={{ dense: true }}
                PaperProps={{ sx: { minWidth: pickerAnchor?.offsetWidth } }}
              >
                {SURFACE_GROUPS.flatMap((group) => [
                  <ListSubheader key={group.category} role="presentation" sx={{ lineHeight: 2.25 }}>
                    {group.category}
                  </ListSubheader>,
                  ...group.surfaces.map((s) => (
                    <MenuItem
                      key={s.surface}
                      selected={s.surface === surface}
                      onClick={() => {
                        setPrefill(null);
                        setSurface(s.surface);
                        setPickerAnchor(null);
                      }}
                    >
                      {s.label}
                    </MenuItem>
                  )),
                ])}
              </Menu>
            </Box>
          )}

          {/* One boundary around the panel body, keyed by what is showing (P1T-153): a panel that
              throws is contained to the body — the widget header and the navigation above stay
              live, so picking another surface, or leaving the ledger, is both escape and retry. */}
          <ErrorBoundary
            resetKey={usageOpen ? "usage" : surface}
            fallback={(error, reset) => <DockErrorFallback error={error} reset={reset} />}
          >
            {/* Remount per surface so each keeps its own independent state. The job forms also
                remount per prefill so a new drill-in always lands its values. */}
            {usageOpen ? (
              <UsagePanel key="usage" />
            ) : surface === "roster" ? (
              <RosterChat key="roster" />
            ) : surface === "ingestion" ? (
              <IngestionPanel key="ingestion" />
            ) : surface === "shortlist" ? (
              <ShortlistPanel
                key="shortlist"
                onRunMatch={(employeeId, jd) => openPrefilled("match", employeeId, jd)}
              />
            ) : surface === "staffing" ? (
              <StaffingPanel
                key="staffing"
                onOpenInMatch={(employeeId, jd) => openPrefilled("match", employeeId, jd)}
                onTailorCv={(employeeId, jd) => openPrefilled("cv-tailoring", employeeId, jd)}
              />
            ) : surface === "roster-scan" ? (
              <RosterScanPanel
                key="roster-scan"
                onOpenInMatch={(employeeId, jd) => openPrefilled("match", employeeId, jd)}
              />
            ) : surface === "bench" ? (
              <BenchPanel key="bench" />
            ) : (
              <AgentJobForm
                key={prefill?.mode === surface ? `${surface}-${prefill.request.employeeId}` : surface}
                mode={surface}
                initial={prefill?.mode === surface ? prefill.request : undefined}
              />
            )}
          </ErrorBoundary>
        </Paper>
      )}
    </>
  );
}
