import { useEffect, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import {
  Box,
  CircularProgress,
  Dialog,
  InputBase,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  ListSubheader,
  Typography,
} from "@mui/material";
import PersonOutlinedIcon from "@mui/icons-material/PersonOutlined";
import SearchIcon from "@mui/icons-material/Search";
import SmartToyIcon from "@mui/icons-material/SmartToy";
import { useNavigate } from "react-router-dom";
import { useExperts } from "../api";
import { apiErrorMessage } from "../api/http";
import { NAV } from "./AppRail";
import { SURFACE_GROUPS } from "./AgentWidget";
import { ErrorNotice } from "./ErrorNotice";
import { openAgentSurface } from "./agent/surfaceRequest";
import type { AgentDock } from "./useAgentDock";
import {
  PALETTE_INPUT_LABEL,
  closeCommandPalette,
  useCommandPaletteHotkey,
  useCommandPaletteOpen,
} from "./useCommandPalette";

/**
 * How many people the palette will list at once.
 *
 * Not a limit on what is *searched* — see the note on {@link PaletteBody} — only on what is drawn.
 * A palette is a keyboard surface: past a handful of rows nobody arrows to the bottom, they type
 * another letter. The count of what was left out is shown rather than hidden, because a silently
 * truncated result set is the one thing this feature must not be.
 */
export const PEOPLE_SHOWN = 7;

/** One runnable row. `hint` is the second line — a job title, or the surface's own group. */
interface PaletteItem {
  key: string;
  label: string;
  hint?: string;
  icon: ReactNode;
  run: () => void;
}

interface PaletteGroup {
  heading: string;
  /** Shown under the heading when the group is not showing everything it matched. */
  note?: string;
  items: PaletteItem[];
}

/**
 * Every whitespace-separated term must appear somewhere in the row's text, in any order — so
 * `grace hop` finds Grace Hopper and so does `hopper g`. An empty query matches everything.
 */
function matchesQuery(query: string, ...fields: (string | null | undefined)[]): boolean {
  const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return true;
  const haystack = fields.filter(Boolean).join(" ").toLowerCase();
  return terms.every((term) => haystack.includes(term));
}

/**
 * The palette's body, mounted only while it is open.
 *
 * That is what makes the roster query honest *and* free: `useExperts` is the same
 * `["experts"]` query the roster page uses, so an already-loaded roster costs nothing and a cold
 * one is fetched on the first ⌘K rather than on every page load.
 *
 * **What "search" means here** (the question P1T-165 was deferred to answer). `GET /api/experts`
 * is unpaged — it returns every active expert in one response, which is why the roster page can
 * be a client-side table at all — so filtering that cached list *is* searching the whole roster.
 * The palette therefore searches exactly what the roster page shows: all of it, drafts excluded,
 * with no new endpoint and no second definition of what a match is. A server search becomes
 * necessary on the day the list endpoint starts paging, and on that day the roster page needs one
 * too; the guard is `Roster_list_returns_every_active_expert_in_one_response` in
 * `tests/Web.Tests/ExpertCrudTests.cs`, which fails the moment that stops being true.
 */
function PaletteBody({ dock }: { dock: AgentDock }) {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const { data: experts, isLoading, isError, error } = useExperts();
  const listRef = useRef<HTMLUListElement | null>(null);

  const groups = useMemo<PaletteGroup[]>(() => {
    const places: PaletteItem[] = NAV.filter((place) => matchesQuery(query, place.label)).map(
      (place) => ({
        key: `place:${place.to}`,
        label: place.label,
        icon: place.icon,
        run: () => navigate(place.to),
      }),
    );

    // People appear once there is something to search for. With an empty query the palette is a
    // list of places to go, and pouring the whole roster into it would bury them.
    const people = query.trim()
      ? (experts ?? []).filter((e) =>
          matchesQuery(query, `${e.firstName} ${e.lastName}`, e.title, e.location, e.email),
        )
      : [];

    // The dock's own vocabulary, read rather than restated: the palette offers exactly the surfaces
    // the picker offers, in the picker's groups, and gains the tenth one for free (P1T-152).
    const surfaces: PaletteItem[] = SURFACE_GROUPS.flatMap((group) =>
      group.surfaces
        .filter((s) => matchesQuery(query, s.label, group.category))
        .map((s) => ({
          key: `surface:${s.surface}`,
          label: s.label,
          hint: group.category,
          icon: <SmartToyIcon fontSize="small" />,
          run: () => openAgentSurface(dock, s.surface),
        })),
    );

    return [
      { heading: "Places", items: places },
      {
        heading: "People",
        note:
          people.length > PEOPLE_SHOWN
            ? `Showing ${PEOPLE_SHOWN} of ${people.length} matches — keep typing`
            : undefined,
        items: people.slice(0, PEOPLE_SHOWN).map((e) => ({
          key: `person:${e.id}`,
          label: `${e.firstName} ${e.lastName}`,
          hint: [e.title, e.location].filter(Boolean).join(" · "),
          icon: <PersonOutlinedIcon fontSize="small" />,
          run: () => navigate(`/experts/${e.id}`),
        })),
      },
      { heading: "Agent surfaces", items: surfaces },
    ].filter((group) => group.items.length > 0);
  }, [query, experts, navigate, dock]);

  // One flat list underneath the headings: the arrow keys move through results, not through groups.
  const flat = useMemo(() => groups.flatMap((g) => g.items), [groups]);
  const activeItem = flat[Math.min(active, flat.length - 1)];

  const activeIndex = activeItem ? flat.indexOf(activeItem) : -1;
  useEffect(() => {
    if (activeIndex < 0) return;
    const rows = listRef.current?.querySelectorAll('[role="option"]');
    // jsdom implements no `scrollIntoView`; it is presentation only, so it is optional here rather
    // than polyfilled in the test setup.
    (rows?.[activeIndex] as HTMLElement | undefined)?.scrollIntoView?.({ block: "nearest" });
  }, [activeIndex]);

  function run(item: PaletteItem) {
    // Closed first: every action navigates or opens something, and a palette still sitting over the
    // thing it just opened is the one outcome nobody wants.
    closeCommandPalette();
    item.run();
  }

  function onKeyDown(e: React.KeyboardEvent) {
    if (flat.length === 0) return;
    if (e.key === "ArrowDown") setActive((i) => (i + 1) % flat.length);
    else if (e.key === "ArrowUp") setActive((i) => (i - 1 + flat.length) % flat.length);
    else if (e.key === "Home") setActive(0);
    else if (e.key === "End") setActive(flat.length - 1);
    else if (e.key === "Enter") {
      if (activeItem) run(activeItem);
    } else return;
    e.preventDefault();
  }

  return (
    <>
      <Box
        sx={{
          display: "flex",
          alignItems: "center",
          gap: 1.5,
          px: 2,
          py: 1.5,
          borderBottom: 1,
          borderColor: "divider",
        }}
      >
        <SearchIcon fontSize="small" sx={{ color: "text.secondary", flexShrink: 0 }} />
        <InputBase
          autoFocus
          fullWidth
          value={query}
          // The highlight goes back to the top on every keystroke, in the handler rather than in an
          // effect on `query`: typing changes what is on offer, and staying on whatever ordinal the
          // previous result set had there would run a row nobody chose.
          onChange={(e) => {
            setQuery(e.target.value);
            setActive(0);
          }}
          onKeyDown={onKeyDown}
          placeholder={PALETTE_INPUT_LABEL}
          // The ARIA 1.2 combobox pattern: the input keeps focus while the list below it is
          // navigated, so the row a person is on is published here rather than being only a colour.
          inputProps={{
            role: "combobox",
            "aria-label": PALETTE_INPUT_LABEL,
            "aria-expanded": true,
            "aria-controls": "command-palette-results",
            "aria-activedescendant": activeItem ? `command-palette-${activeItem.key}` : undefined,
            "aria-autocomplete": "list",
          }}
        />
        <Typography variant="caption" sx={{ color: "text.secondary", flexShrink: 0 }}>
          esc
        </Typography>
      </Box>

      <Box sx={{ maxHeight: 360, overflowY: "auto" }}>
        <ErrorNotice
          message={isError ? "Could not load the roster." : null}
          detail={isError ? apiErrorMessage(error) : null}
          sx={{ m: 1 }}
        />

        <List
          id="command-palette-results"
          role="listbox"
          aria-label="Results"
          ref={listRef}
          dense
          sx={{ py: 0 }}
        >
          {groups.map((group) => [
            // `presentation` for the same reason the dock's picker uses it: a heading that keeps a
            // listbox role would be announced as something you can pick, and it is not (P1T-152).
            <ListSubheader key={group.heading} role="presentation" sx={{ lineHeight: 2.25 }}>
              {group.heading}
            </ListSubheader>,
            group.note ? (
              <Typography
                key={`${group.heading}-note`}
                role="presentation"
                variant="caption"
                sx={{ display: "block", px: 2, pb: 0.5, color: "text.secondary" }}
              >
                {group.note}
              </Typography>
            ) : null,
            ...group.items.map((item) => (
              <ListItemButton
                key={item.key}
                data-key={item.key}
                id={`command-palette-${item.key}`}
                role="option"
                aria-selected={item.key === activeItem?.key}
                selected={item.key === activeItem?.key}
                // Hover moves the highlight too, so the keyboard's idea of "the active row" and the
                // pointer's never disagree about which one Enter would run.
                onMouseMove={() => setActive(flat.indexOf(item))}
                onClick={() => run(item)}
              >
                <ListItemIcon sx={{ minWidth: 0, mr: 2, color: "inherit" }}>{item.icon}</ListItemIcon>
                <ListItemText
                  primary={item.label}
                  secondary={item.hint || undefined}
                  primaryTypographyProps={{ variant: "body2", noWrap: true }}
                  secondaryTypographyProps={{ variant: "caption", noWrap: true }}
                />
              </ListItemButton>
            )),
          ])}
        </List>

        {flat.length === 0 && (
          <Box sx={{ px: 2, py: 3 }}>
            {isLoading && query.trim() ? (
              <Box sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
                <CircularProgress size={16} />
                <Typography variant="body2" color="text.secondary">
                  Loading the roster…
                </Typography>
              </Box>
            ) : (
              <Typography variant="body2" color="text.secondary">
                No matches for “{query.trim()}”
              </Typography>
            )}
          </Box>
        )}
      </Box>
    </>
  );
}

/**
 * The ⌘K Command Palette (P1T-165): one keystroke to a place, a person, or an Agent Surface.
 *
 * Mounted beside the dock rather than inside the rail, even though the rail carries the visible
 * trigger. The palette has to be reachable with no rail on screen — below `md` the rail is a closed
 * drawer most of the time — and it acts on the dock as well as on the routes, so the rail is where
 * it is *advertised*, not where it lives.
 */
export default function CommandPalette({ dock }: { dock: AgentDock }) {
  const open = useCommandPaletteOpen();
  useCommandPaletteHotkey();

  return (
    <Dialog
      open={open}
      onClose={closeCommandPalette}
      fullWidth
      maxWidth="sm"
      aria-label="Command palette"
      // Near the top, the way every palette sits: centred vertically it would jump the eye down
      // from the shortcut that opened it, and the list grows downwards from a fixed edge instead of
      // resizing around the middle as results arrive.
      sx={{ "& .MuiDialog-container": { alignItems: "flex-start" } }}
      PaperProps={{ sx: { mt: "10vh", overflow: "hidden" } }}
    >
      {/* Only mounted while open, which is what keeps the roster query off the cold path. */}
      <PaletteBody dock={dock} />
    </Dialog>
  );
}
