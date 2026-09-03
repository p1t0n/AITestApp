// The component overrides — where the *look* lives. `tokens.ts` says what the colours are and
// `index.ts` maps them onto MUI's palette; this file is the only place that decides what a button,
// a table row or a menu made of those colours looks like.
//
// The load-bearing rule of this layer (`manuals/spa-design-system.md` §3): **a look needed twice
// belongs here, not in a third `sx`.** The app's ~150 `sx` blocks are spacing and layout and stay
// that way. Anything a reviewer would recognise as "the app's style" — density, hairlines, where
// the accent is allowed — is a default prop or a style override in this file, so it is changed in
// one place and cannot drift per page.
//
// Three defaults do most of the work, and each has a reason a component-level `sx` could not:
//   * `MuiPaper` is `outlined` + `elevation: 0` — borders separate, shadows are the exception.
//   * everything interactive is `size: "small"` — the app was already writing that 100+ times.
//   * the only shadow in the system is `relief.float`, and it is reserved for surfaces that
//     genuinely float (menu, dialog, autocomplete popup), because a border cannot say "above".
//
// There is deliberately **no `MuiTabs` override**: the app renders no tabs anywhere. The agent
// dock's surface picker is a grouped `Menu` (P1T-152) and stays one.
import type { Components, Theme } from "@mui/material/styles";
import { alpha } from "@mui/material/styles";
import { tokens } from "./tokens";
import type { ThemeModeTokens } from "./tokens";

/**
 * How strongly a semantic role tints the panel of a standard `Alert`, and how strongly it draws
 * that panel's edge. Alpha rather than a per-severity token, because the tint has to sit over any
 * of the three surfaces — and because five more colour tokens per mode is how a palette starts
 * needing a palette. Both are asserted against real composites in `components.test.tsx`.
 */
const ALERT_TINT = 0.14;
const ALERT_EDGE = 0.4;

/** The four `Alert` severities, in the order MUI names its own style slots. */
const SEVERITIES = ["error", "warning", "info", "success"] as const;

/**
 * A tinted panel per severity: the role's colour as an edge and a wash, the *text* left as ordinary
 * body text. MUI's own standard `Alert` computes its colours with `lighten`/`darken` off
 * `palette[severity].light`, which in this palette is a saturated mid-step (see `tokens.ts`) —
 * `error.light` is a fill white reads on, so `lighten(…, 0.9)` of it is not a colour anyone chose.
 * The icon keeps the role colour and carries the semantics; the message stays at `text.primary`
 * so it clears 4.5:1 on every surface without depending on how bright the role happens to be.
 */
function alertTints(t: ThemeModeTokens) {
  const overrides: Record<string, object> = {};
  for (const severity of SEVERITIES) {
    const role = t[severity];
    const slot = `standard${severity[0].toUpperCase()}${severity.slice(1)}`;
    overrides[slot] = {
      backgroundColor: alpha(role.main, ALERT_TINT),
      border: `1px solid ${alpha(role.main, ALERT_EDGE)}`,
      color: t.text.primary,
      "& .MuiAlert-icon": { color: role.main },
    };
  }
  return overrides;
}

export function componentOverrides(t: ThemeModeTokens): Components<Theme> {
  return {
    // ---- surfaces -------------------------------------------------------------------------

    MuiPaper: {
      // The single biggest reason the app read as stock MUI: every panel floated on a grey drop
      // shadow. A bordered, flat panel is the default now, so a *new* Paper is right by default
      // and the exceptions are the ones that have to say so.
      defaultProps: { elevation: 0, variant: "outlined" },
      variants: [
        {
          // A `well` is a Paper that carries its own fill — a message bubble, a degradation note,
          // a read-only block inside a panel. It is neither `outlined` (a hairline on a coloured
          // fill reads as a defect) nor `elevation` (there is no elevation; on dark mode that
          // variant also paints MUI's white overlay gradient). MUI's own `variants` API rather
          // than a fourth `sx` copy of the same three declarations — the rule at the top of this
          // file, applied to the 11 sites that were doing it by hand.
          props: { variant: "well" },
          style: {
            border: 0,
            boxShadow: "none",
            backgroundImage: "none",
            // The default fill. Sites that mean something semantic (a warning note) still set
            // `bgcolor`; sites that just wanted "a step in from the panel" now say nothing, and
            // three of them stop faking it with `action.hover`, which is an interaction wash.
            backgroundColor: t.surface.raised,
          },
        },
      ],
    },

    // The shell's top bar is *not* restyled here: P1T-161 replaces it with the left rail, and
    // restyling a component two days before deleting it is work with a half-life. All it needs is
    // to not inherit the new outlined default as a box drawn around the whole bar.
    MuiAppBar: { styleOverrides: { root: { border: 0 } } },

    // ---- controls -------------------------------------------------------------------------

    MuiButton: {
      defaultProps: { size: "small", disableElevation: true },
      styleOverrides: {
        // Accent on the primary action only. `outlined` and `text` primary buttons are the app's
        // *secondary* actions — Cancel, Deactivate, a row action — and MUI paints them accent-blue
        // by default, which is how an accent stops meaning anything. They read as neutral chrome
        // now; `contained` keeps the accent, and so does the focus ring on all of them.
        // Keyed per colour (`…Primary`) rather than on the shared `outlined`/`text` slot so that
        // `color="error"` still looks like a destructive action.
        outlinedPrimary: {
          color: t.text.primary,
          borderColor: t.surface.outline,
          "&:hover": { borderColor: t.text.secondary, backgroundColor: t.action.hover },
        },
        textPrimary: {
          color: t.text.primary,
          "&:hover": { backgroundColor: t.action.hover },
        },
      },
    },

    MuiIconButton: {
      // Not on the ticket's list, and the clearest case for being here: 19 call sites were already
      // writing `size="small"`, which makes it the app's default in everything but name.
      defaultProps: { size: "small" },
    },

    MuiTextField: { defaultProps: { size: "small" } },

    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          // Hover is a *step*, not the destination: rest is the control boundary, hover is the
          // stronger neutral, focus is the accent at 2px. MUI's own hover jumps straight to
          // `text.primary`, which makes hover louder than focus.
          "&:hover .MuiOutlinedInput-notchedOutline": { borderColor: t.text.secondary },
          "&.Mui-focused .MuiOutlinedInput-notchedOutline": { borderWidth: 2 },
        },
        // The consumer `surface.outline` was written for (P1T-159 shipped the token with nothing
        // pointing at it). MUI's default notched outline is `rgba(255,255,255,0.23)` — about
        // 2.1:1 — so until this line existed the app's inputs did not meet WCAG 1.4.11's 3:1 for
        // the boundary that identifies a control.
        notchedOutline: { borderColor: t.surface.outline },
      },
    },

    MuiAutocomplete: {
      defaultProps: { size: "small" },
      // Its popup is a Paper, so it is already bordered and flat; what it needs is the one thing a
      // border cannot express, which is that it sits *above* the field it came from.
      styleOverrides: { paper: { boxShadow: t.relief.float } },
    },

    MuiChip: {
      defaultProps: { size: "small" },
      // Radius one step in from the surface it sits on: a chip is inside a control, not a panel.
      // Colours are left entirely alone — `variant="outlined"` chips carry their role's border and
      // a blanket `outlined` override here would flatten all five of them to one grey.
      styleOverrides: { root: { borderRadius: tokens.radius.small } },
    },

    // ---- tables ---------------------------------------------------------------------------

    MuiTable: {
      // Compact rows everywhere. `size` is read through context by every cell in the table, so one
      // default here is the whole density change — and it is why `TableCell` needs no `padding`.
      defaultProps: { size: "small" },
    },

    MuiTableCell: {
      styleOverrides: {
        root: { borderColor: t.divider },
        head: {
          backgroundColor: t.surface.raised,
          color: t.text.secondary,
          fontWeight: 600,
          whiteSpace: "nowrap",
        },
        sizeSmall: { padding: "6px 12px" },
      },
    },

    MuiTableRow: {
      styleOverrides: {
        // The last row's rule and the panel's own border are the same line drawn twice, 1px apart.
        root: { "&:last-child td, &:last-child th": { borderBottom: 0 } },
      },
    },

    // ---- overlays -------------------------------------------------------------------------

    MuiDialog: {
      styleOverrides: {
        paper: {
          // A modal is the one place a shadow is doing real work: it is above everything, over a
          // backdrop, and a border alone would leave it looking pasted onto the page.
          boxShadow: t.relief.float,
          backgroundImage: "none",
        },
      },
    },
    MuiDialogTitle: { styleOverrides: { root: { fontSize: "1.0625rem", fontWeight: 600, padding: "16px 20px 8px" } } },
    MuiDialogContent: { styleOverrides: { root: { padding: "8px 20px" } } },
    MuiDialogActions: { styleOverrides: { root: { padding: "12px 20px", gap: 8 } } },

    MuiMenu: {
      styleOverrides: {
        paper: { boxShadow: t.relief.float, backgroundImage: "none" },
        list: { paddingTop: 4, paddingBottom: 4 },
      },
    },
    MuiMenuItem: {
      styleOverrides: {
        root: {
          minHeight: 32,
          fontSize: "0.8125rem",
          "&.Mui-selected": {
            backgroundColor: t.action.selected,
            "&:hover": { backgroundColor: t.action.selected },
          },
        },
      },
    },

    MuiTooltip: {
      styleOverrides: {
        // MUI's tooltip is a grey slab that belongs to neither mode. This one is the raised
        // surface with the app's own hairline, so it reads as a small piece of the app.
        tooltip: {
          backgroundColor: t.surface.raised,
          color: t.text.primary,
          border: `1px solid ${t.divider}`,
          borderRadius: tokens.radius.small,
          boxShadow: t.relief.float,
          fontSize: "0.75rem",
          padding: "4px 8px",
        },
        arrow: { color: t.surface.raised },
      },
    },

    MuiAlert: {
      styleOverrides: {
        root: { borderRadius: tokens.radius.medium, alignItems: "center" },
        ...alertTints(t),
      },
    },

    // ---- what the rail is built from (P1T-161) --------------------------------------------

    MuiDrawer: {
      styleOverrides: {
        // A drawer has one edge that faces the app; the other three are the viewport. The outlined
        // Paper default would draw all four.
        paper: { border: 0, backgroundImage: "none" },
        paperAnchorLeft: { borderRight: `1px solid ${t.divider}` },
        paperAnchorRight: { borderLeft: `1px solid ${t.divider}` },
      },
    },

    MuiListItemButton: {
      styleOverrides: {
        root: {
          borderRadius: tokens.radius.small,
          "&.Mui-selected": {
            backgroundColor: t.action.selected,
            "&:hover": { backgroundColor: t.action.selected },
          },
        },
      },
    },
  };
}
