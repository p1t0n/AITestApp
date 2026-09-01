import { useState } from "react";
import {
  Box,
  Button,
  Divider,
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
import { DOCK_MIN_WIDTH, maxDockWidth, useDockPush, type AgentDock } from "./useAgentDock";
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
import { useAgentSurfaceRequest } from "./agent/surfaceRequest";

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
export const SURFACE_GROUPS: { category: string; surfaces: { surface: Surface; label: string }[] }[] = [
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

/**
 * The resize handle's accessible name. It is a real control now rather than a bare `col-resize`
 * strip, so it needs a name, a value, and keys — see {@link RESIZE_STEP}.
 */
export const RESIZE_HANDLE_LABEL = "Resize the agents dock";

/**
 * How much one arrow key moves the dock's edge, and ×4 with Shift. Small enough to place the edge
 * precisely, large enough that crossing a 400px range is a held key rather than a hundred presses.
 */
export const RESIZE_STEP = 24;

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
  // a staffing card) jump to the target surface with the expert + JD pre-filled. Cleared on any
  // manual navigation — picking a surface, or opening the ledger — so a stale prefill never
  // resurfaces later.
  const [prefill, setPrefill] = useState<{
    mode: "cv-tailoring" | "match";
    request: AgentJobRequest;
  } | null>(null);
  function openPrefilled(target: "cv-tailoring" | "match", expertId: string, jobDescription: string) {
    setPrefill({ mode: target, request: { expertId, jobDescription } });
    setUsageOpen(false);
    setSurface(target);
  }

  // The ⌘K palette jumps straight to a surface (P1T-165). It arrives as a name rather than as a
  // `Surface`, so an unrecognised one is ignored here — the channel carries the message and this is
  // the only place that knows the vocabulary. Treated exactly like any other navigation: a stale
  // prefill and an open ledger both get out of the way first.
  useAgentSurfaceRequest((name) => {
    if (!(name in SURFACE_LABELS)) return;
    setPrefill(null);
    setUsageOpen(false);
    setSurface(name as Surface);
  });

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

  // The same edge, from the keyboard. Until P1T-163 the handle was a mouse-only affordance with no
  // visible existence at all: a 6px strip whose entire declaration was `cursor: col-resize`, so a
  // person who does not hover — or does not use a mouse — had no way to know the dock resizes, and
  // no way to do it. The window-splitter pattern gives it a name, a value and arrow keys; the
  // clamping stays in the hook, so both input paths land on exactly the same rule.
  //
  // Left grows and right shrinks because the dock is anchored to the *right* edge: the key moves
  // the handle, not the width, which is what a person watching the edge expects.
  const onResizeKey = (e: React.KeyboardEvent) => {
    const step = e.shiftKey ? RESIZE_STEP * 4 : RESIZE_STEP;
    if (e.key === "ArrowLeft") dock.setWidth(dock.width + step);
    else if (e.key === "ArrowRight") dock.setWidth(dock.width - step);
    else if (e.key === "Home") dock.setWidth(DOCK_MIN_WIDTH);
    else if (e.key === "End") dock.setWidth(maxDockWidth());
    else return;
    e.preventDefault();
  };

  const dockedWide = dock.docked && !dock.isNarrow;
  const dockedNarrow = dock.docked && dock.isNarrow;

  // The assistant is the operator's tool, not part of any document they print — the same reasoning
  // the rail applies to itself (`AppRail.tsx`). It had no print rule until P1T-166 watched Chromium
  // resolve the cascade: both of these are `position: fixed`, so they are painted *over* the page
  // rather than laid out in it, and the bubble was landing in the bottom-right corner of the first
  // sheet of every printed artifact in this app — a robot icon on a client's CV. The docked panel
  // is the worse of the two for a reason P1T-160 introduced: print drops background colours but
  // keeps borders, so its `borderLeft` would rule a hairline down the page even where the surface
  // colour vanished. Colocated in the `sx` of whoever renders the element (P1T-154), so removing
  // the surface and removing its print behaviour stay the same edit.
  const hideInPrint = { "@media print": { display: "none" } } as const;

  const panelSx = !dock.docked
    ? {
        bottom: 96,
        right: 24,
        width: 460,
        maxWidth: "calc(100vw - 48px)",
        height: 620,
        maxHeight: "calc(100vh - 140px)",
        // 12px. `sx` multiplies by `theme.shape.borderRadius`, which the token layer moved 4 → 8,
        // so the `3` this used to be is now 24px — the same doubling the Papers' `borderRadius: 2`
        // had, and those just dropped the entry because 8px *is* the theme's radius.
        borderRadius: 1.5,
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
          sx={{ position: "fixed", bottom: 24, right: 24, zIndex: 1300, ...hideInPrint }}
        >
          <SmartToyIcon />
        </Fab>
      )}

      {dock.open && (
        <Paper
          variant="elevation"
          elevation={dockedWide ? 4 : 8}
          square={dock.docked}
          sx={{
            position: "fixed",
            display: "flex",
            flexDirection: "column",
            zIndex: 1300,
            overflow: "hidden",
            ...panelSx,
            // Last, and it has to be: this element declares `display: flex` above, and a media
            // query carries no extra specificity — only source order separates the two.
            ...hideInPrint,
          }}
        >
          {dockedWide && (
            <Box
              // The ARIA window-splitter pattern: a focusable `separator` carries a value, so a
              // screen reader announces the dock's width and the arrow keys have somewhere to
              // report to. `tabIndex` is what makes it discoverable without a mouse at all.
              role="separator"
              aria-orientation="vertical"
              aria-label={RESIZE_HANDLE_LABEL}
              aria-valuenow={Math.round(dock.width)}
              aria-valuemin={DOCK_MIN_WIDTH}
              aria-valuemax={Math.round(maxDockWidth())}
              tabIndex={0}
              onMouseDown={startResize}
              onKeyDown={onResizeKey}
              sx={{
                position: "absolute",
                left: 0,
                top: 0,
                bottom: 0,
                width: 10,
                cursor: "col-resize",
                zIndex: 2,
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                // The affordance itself: a grip that is *always* drawn — a hairline in `divider`,
                // the same line the panel's own edge uses — and that grows and takes the accent on
                // hover or keyboard focus. The old strip painted `primary.light` across its whole
                // width on hover and nothing at rest, which is a hover-only affordance: invisible
                // until you have already found it. The focus ring comes from the baseline's
                // `html *:focus-visible` and is deliberately not overridden here.
                "&::after": {
                  content: '""',
                  width: 2,
                  height: 28,
                  borderRadius: 1,
                  bgcolor: "divider",
                  transition: (t) =>
                    t.transitions.create(["background-color", "height"], { duration: t.transitions.duration.short }),
                },
                "&:hover::after, &:focus-visible::after": { bgcolor: "primary.main", height: 72 },
              }}
            />
          )}

          {/* One bar, two rows: what the panel *is* on top, where it is *pointed* underneath.
              Before P1T-163 these were an accent-blue slab and a separate bordered strip, which is
              what made the dock read as bolted on — the app's accent appears on the primary action
              and the focus ring and essentially nowhere else (`manuals/spa-design-system.md` §3),
              and a solid accent header is the largest possible violation of that rule. It is the
              raised step of the surface ramp now, with one hairline under the pair rather than a
              rule between them, so the two rows are visibly one piece of chrome.

              Navigation chrome is inside this bar and OUTSIDE the boundary below: whatever a panel
              does, the way out of it has to keep rendering. */}
          <Box
            sx={{
              flexShrink: 0,
              bgcolor: "surface.raised",
              borderBottom: 1,
              borderColor: "divider",
            }}
          >
            <Stack
              direction="row"
              alignItems="center"
              spacing={1}
              sx={{ flexWrap: "nowrap", pl: 1.5, pr: 0.5, py: 0.5, minHeight: 40 }}
            >
              <SmartToyIcon fontSize="small" sx={{ color: "text.secondary", flexShrink: 0 }} />
              {/* `noWrap` + `minWidth: 0` is what holds the "does not wrap or clip at 360px"
                  claim: the title is the only elastic thing in the row, so it gives up its width
                  to the controls instead of pushing them onto a second line. */}
              <Typography variant="subtitle2" noWrap sx={{ flex: 1, minWidth: 0 }}>
                Agents
              </Typography>
              <Stack direction="row" alignItems="center" sx={{ flexShrink: 0 }}>
                <Tooltip title="Token usage">
                  <IconButton
                    aria-label="Token usage"
                    aria-pressed={usageOpen}
                    onClick={() => {
                      setPrefill(null);
                      setUsageOpen((o) => !o);
                    }}
                    // `color="inherit"` rather than MUI's `action.active`, which this palette
                    // resolves to flat white in dark mode — louder than the title beside it and a
                    // colour nobody chose (the trap P1T-162 hit on the CV page's Back button).
                    color={usageOpen ? "primary" : "inherit"}
                  >
                    <DataUsageIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
                {/* The ledger is a peek at state; the two beside it are window controls. One
                    hairline says so, which is cheaper than a gap nobody reads as grouping. */}
                <Divider orientation="vertical" flexItem sx={{ mx: 0.5, my: 0.75 }} />
                <Tooltip title={dock.docked ? "Float" : "Dock to side"}>
                  <IconButton
                    aria-label={dock.docked ? "Float" : "Dock to side"}
                    color="inherit"
                    onClick={() => dock.setDocked(!dock.docked)}
                  >
                    {dock.docked ? (
                      <CloseFullscreenIcon fontSize="small" />
                    ) : (
                      <OpenInFullIcon fontSize="small" />
                    )}
                  </IconButton>
                </Tooltip>
                {/* Named at last: an icon-only button with only a Tooltip-less icon in it has no
                    accessible name at all, so the dock's own close control was unreachable by
                    name — the one control in this header that every other one implies. */}
                <Tooltip title="Close">
                  <IconButton aria-label="Close the agents assistant" color="inherit" onClick={dock.close}>
                    <CloseIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </Stack>
            </Stack>

            {usageOpen ? (
              <Box sx={{ px: 1, pb: 0.75, display: "flex", alignItems: "center", gap: 1 }}>
                <Button startIcon={<ArrowBackIcon />} onClick={() => setUsageOpen(false)}>
                  Back to {SURFACE_LABELS[surface]}
                </Button>
                <Typography variant="body2" color="text.secondary" noWrap>
                  Token usage
                </Typography>
              </Box>
            ) : (
              <Box sx={{ px: 1, pb: 0.75 }}>
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
                  // A bordered control rather than a bare text button: it is the panel's one
                  // navigation control and it now sits on the raised step, where a borderless
                  // label reads as a heading rather than as something to press.
                  sx={{
                    justifyContent: "space-between",
                    textTransform: "none",
                    px: 1.5,
                    bgcolor: "background.paper",
                    border: 1,
                    borderColor: "divider",
                    "&:hover": { bgcolor: "background.paper", borderColor: "surface.outline" },
                  }}
                >
                  <Box component="span" sx={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                    {SURFACE_LABELS[surface]}
                  </Box>
                </Button>
                <Menu
                  anchorEl={pickerAnchor}
                  open={!!pickerAnchor}
                  onClose={() => setPickerAnchor(null)}
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
          </Box>

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
                onRunMatch={(expertId, jd) => openPrefilled("match", expertId, jd)}
              />
            ) : surface === "staffing" ? (
              <StaffingPanel
                key="staffing"
                onOpenInMatch={(expertId, jd) => openPrefilled("match", expertId, jd)}
                onTailorCv={(expertId, jd) => openPrefilled("cv-tailoring", expertId, jd)}
              />
            ) : surface === "roster-scan" ? (
              <RosterScanPanel
                key="roster-scan"
                onOpenInMatch={(expertId, jd) => openPrefilled("match", expertId, jd)}
              />
            ) : surface === "bench" ? (
              <BenchPanel key="bench" />
            ) : (
              <AgentJobForm
                key={prefill?.mode === surface ? `${surface}-${prefill.request.expertId}` : surface}
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
