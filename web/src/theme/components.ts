// The component overrides — where the *look* lives. `tokens.ts` says what the colours and the
// relief pairs are and `index.ts` maps them onto MUI's palette; this file is the only place that
// decides what a button, a table row or a menu made of those looks like.
//
// The load-bearing rule of this layer (`manuals/spa-design-system.md` §3): **a look needed twice
// belongs here, not in a third `sx`.** The app's ~150 `sx` blocks are spacing and layout and stay
// that way. Anything a reviewer would recognise as "the app's style" — density, depth, where the
// accent is allowed — is a default prop or a style override in this file, so it is changed in one
// place and cannot drift per page.
//
// Three defaults do most of the work, and each has a reason a component-level `sx` could not:
//   * `MuiPaper` is extruded relief — depth is a shadow pair now, and a panel that asks for
//     nothing is lifted. This inverts what this file said until the neumorphic reversal
//     (`manuals/adr-neumorphic-reskin.md`), which was `outlined` + `elevation: 0`.
//   * everything interactive is `size: "small"` — the app was already writing that 100+ times.
//   * **Relief Depth** is enforced here rather than remembered per call site: two levels, then a
//     flat fill and a hairline. MUI cannot tell a component its own nesting depth — but a
//     descendant selector can, which is what makes the ceiling a rule and not an intention.
//
// There is deliberately **no `MuiTabs` override**: the app renders no tabs anywhere. The agent
// dock's surface picker is a grouped `Menu` (P1T-152) and stays one.
import type { Components, Theme } from "@mui/material/styles";
import { alpha } from "@mui/material/styles";
import { tokens } from "./tokens";
import type { ColorRole, ThemeModeTokens } from "./tokens";
import type { ThemeMode } from "./mode";

/**
 * How strongly a semantic role tints the panel of a standard `Alert`, and how strongly it draws
 * that panel's edge. Alpha rather than a per-severity token, because the tint has to sit over any
 * of the three surfaces — and because five more colour tokens per mode is how a palette starts
 * needing a palette. Both are asserted against real composites in `components.test.tsx`.
 */
const ALERT_TINT = 0.14;
const ALERT_EDGE = 0.4;

/**
 * The same idea for a Chip, which is a tag: a wash of the role, inked in the role. Lighter than the
 * Alert's tint, and measured rather than chosen — the ink sits *on* its own wash, so every step of
 * alpha costs label contrast. At `0.16` dark-mode `warning` lands at 4.1:1; this is what clears AA
 * for all five roles in both modes.
 */
const TAG_TINT = 0.1;
const TAG_EDGE = 0.4;

/** How far the accent washes a hovered table row. Low, because it lands under body text. */
const ROW_WASH = 0.08;

/** The four `Alert` severities, in the order MUI names its own style slots. */
const SEVERITIES = ["error", "warning", "info", "success"] as const;

/** The five roles a Chip can carry, by the class MUI puts on it. */
const TAG_ROLES = ["primary", "error", "warning", "info", "success"] as const;

/**
 * The step of a role that reads as *text* — slice ①'s "readable step", and the reason this file
 * now needs to know which mode it is building. In dark mode the bright `main` reads on every
 * surface; in light mode it does not, and amber is the case that proves it: `#F59E0B` is 1.9:1 on
 * `#EEF1F8`, so a label or a boundary drawn in it would be decoration pretending to be text.
 * `tokens.contrast.test.ts` holds the floor this satisfies.
 */
function ink(role: ColorRole, mode: ThemeMode): string {
  return mode === "light" ? role.dark : role.main;
}

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

/**
 * The tag treatment, keyed per role so that five colours stay five colours: a wash of the role as
 * the ground, the role's readable step as the ink, and the same step at `TAG_EDGE` for an outlined
 * chip's border. The pairing is a composite — ink over wash over panel — and is asserted as one.
 */
function tagColours(t: ThemeModeTokens, mode: ThemeMode) {
  const overrides: Record<string, object> = {};
  for (const name of TAG_ROLES) {
    const role = t[name];
    const label = ink(role, mode);
    overrides[`&.MuiChip-color${name[0].toUpperCase()}${name.slice(1)}`] = {
      color: label,
      "&.MuiChip-filled": { backgroundColor: alpha(role.main, TAG_TINT) },
      "&.MuiChip-outlined": { borderColor: alpha(label, TAG_EDGE) },
      "& .MuiChip-deleteIcon": { color: label },
    };
  }
  return overrides;
}

export function componentOverrides(t: ThemeModeTokens, mode: ThemeMode): Components<Theme> {
  /**
   * Level three, and everything below it: a flat fill and a hairline. This is the whole cost of
   * **Relief Depth**, and it is written once here because it is applied by *selector* rather than
   * by any component asking for it.
   */
  const flat = {
    boxShadow: "none",
    backgroundColor: t.surface.raised,
    backgroundImage: "none",
    border: `1px solid ${t.divider}`,
  };

  /** Above the page rather than part of it: the largest pair, plus the backdrop. */
  const float = {
    boxShadow: t.relief.float,
    backdropFilter: t.relief.floatBackdrop,
    backgroundImage: "none",
  };

  return {
    // ---- surfaces -------------------------------------------------------------------------

    MuiPaper: {
      // `elevation: 0` keeps MUI's own shadow scale and its dark-mode overlay gradient out of the
      // way; the relief below is what depth means now. `variant` is left at MUI's default rather
      // than the `outlined` this used to force — a bordered panel is no longer the resting state.
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: {
          border: 0,
          backgroundImage: "none",
          boxShadow: t.relief.extruded,
          // Relief Depth, as a rule the DOM enforces: a Paper with two Paper ancestors is past the
          // ceiling, whatever variants got it there, so it stops being relief. Written as a
          // descendant selector because that is the one thing that knows the answer — a component
          // cannot see its own nesting, which is exactly why the old three-step ramp was a matter
          // of remembering rather than a rule.
          "& .MuiPaper-root .MuiPaper-root": flat,
        },
      },
      variants: [
        {
          // A `well` is the inset half of Relief: a message bubble, a search field, a read-only
          // block pressed into the panel around it. It used to be a flat fill; under the reversal
          // the fill alone is what a level-three surface gets, and a Well is the shadow.
          props: { variant: "well" },
          style: {
            border: 0,
            backgroundImage: "none",
            boxShadow: t.relief.inset,
            // The page ground shows through what is pressed into a panel. `surface.raised` is not
            // this any more — it is the flat fill of the level below the ceiling.
            backgroundColor: t.surface.page,
            // Nothing keeps relief inside a Well. Inset within inset has no physical reading, and
            // it is reachable one level earlier than the descendant count would catch it.
            "& .MuiPaper-root": flat,
          },
        },
        // MUI's elevation numbers still mean what they say, so they are the app's way of asking
        // for Float without a second vocabulary: the agent dock (4 docked, 8 floating) and the
        // error boundary's panel are the only callers, and all three genuinely sit above the page.
        { props: { elevation: 4 }, style: float },
        { props: { elevation: 8 }, style: float },
      ],
    },

    // A bar that spans the viewport is part of the ground, not a card lifted off it — and P1T-161
    // replaces this one with the left rail anyway, so it gets the minimum: no inherited relief and
    // no box drawn around the whole bar.
    MuiAppBar: { styleOverrides: { root: { border: 0, boxShadow: "none" } } },

    // ---- controls -------------------------------------------------------------------------

    MuiButton: {
      defaultProps: { size: "small", disableElevation: true },
      styleOverrides: {
        root: {
          boxShadow: t.relief.extrudedSmall,
          // The press. The shadow flip alone reads as a colour change; the 2px is what makes a
          // finger believe it. Inside §8's ≤150ms ceiling, which did not need raising.
          "&:active": { boxShadow: t.relief.insetSmall, transform: "translateY(2px)" },
          "&.Mui-disabled": { boxShadow: "none" },
        },
        // Accent on the primary action only. `outlined` and `text` primary buttons are the app's
        // *secondary* actions — Cancel, Deactivate, a row action — and MUI paints them accent by
        // default, which is how an accent stops meaning anything. They read as neutral chrome now;
        // `contained` keeps the accent, and so does the focus ring on all of them. Keyed per colour
        // (`…Primary`) rather than on the shared slot so `color="error"` still looks destructive.
        outlinedPrimary: {
          color: t.text.primary,
          borderColor: t.surface.outline,
          "&:hover": { borderColor: t.text.secondary, backgroundColor: t.action.hover },
        },
        textPrimary: {
          color: t.text.primary,
          // A text button is a label, not a panel: it has nothing to be lifted off.
          boxShadow: "none",
          "&:hover": { backgroundColor: t.action.hover },
        },
      },
    },

    MuiIconButton: {
      // Not on the original ticket's list, and the clearest case for being here: 19 call sites were
      // already writing `size="small"`, which makes it the app's default in everything but name.
      defaultProps: { size: "small" },
      styleOverrides: {
        root: {
          "&:active": { transform: "translateY(1px)" },
        },
      },
    },

    MuiTextField: { defaultProps: { size: "small" } },

    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          // A field is a Well: pressed into the panel, on the page's own ground. This is the
          // treatment the prototype ships with `border: 1px solid transparent` — the shadow doing
          // the whole job — and the boundary below is the part we do not ship without.
          boxShadow: t.relief.insetSmall,
          backgroundColor: t.surface.page,
          // Hover is a *step*, not the destination: rest is the control boundary, hover is the
          // stronger neutral, focus is the accent at 2px. MUI's own hover jumps straight to
          // `text.primary`, which makes hover louder than focus.
          "&:hover .MuiOutlinedInput-notchedOutline": { borderColor: t.text.secondary },
          "&.Mui-focused .MuiOutlinedInput-notchedOutline": {
            borderWidth: 2,
            borderColor: t.focusRing,
          },
        },
        // The consumer `surface.outline` was written for (P1T-159 shipped the token with nothing
        // pointing at it), and neumorphism is why it now matters more, not less: 1.4.11 wants a
        // boundary that can be *measured*, and a shadow edge has nothing to measure. The relief
        // above says "field"; this line is what proves it.
        notchedOutline: { borderColor: t.surface.outline },
      },
    },

    MuiAutocomplete: {
      defaultProps: { size: "small" },
      // Its popup sits above the field it came from, which is the one thing relief at rest cannot
      // say — every panel is extruded now.
      styleOverrides: { paper: float },
    },

    MuiChip: {
      defaultProps: { size: "small" },
      styleOverrides: {
        root: {
          // A tag, in the prototype's sense: mono, uppercase, a pill rather than a panel. The
          // radius steps are for surfaces and controls; a tag is neither.
          borderRadius: 999,
          fontFamily: tokens.type.fontFamilyMono,
          textTransform: "uppercase",
          letterSpacing: "0.04em",
          fontWeight: 600,
          boxShadow: "none",
          // A chip with no `color` is not a semantic tag, and MUI's default fill for one is
          // `action.selected` — which is amber now. Neutral by default, coloured on request.
          "&.MuiChip-filled": { backgroundColor: t.surface.raised, color: t.text.primary },
          ...tagColours(t, mode),
        },
      },
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
          // The **Eyebrow** treatment, which is what promoted mono to a UI role in slice ①: a
          // column name labels a column of values, and it lines up with the next one.
          fontFamily: tokens.type.fontFamilyMono,
          textTransform: "uppercase",
          letterSpacing: "0.06em",
          fontSize: "0.6875rem",
          fontWeight: 700,
          whiteSpace: "nowrap",
        },
        sizeSmall: { padding: "6px 12px" },
      },
    },

    MuiTableRow: {
      styleOverrides: {
        root: {
          // The last row's rule and the panel's own edge are the same line drawn twice, 1px apart.
          "&:last-child td, &:last-child th": { borderBottom: 0 },
          // The one place the accent touches a whole row. A wash, not a fill: body text sits on
          // top of it, so what has to hold is the composite, and that is what is asserted.
          "&.MuiTableRow-hover:hover": { backgroundColor: alpha(t.primary.main, ROW_WASH) },
        },
      },
    },

    // ---- overlays -------------------------------------------------------------------------

    MuiDialog: { styleOverrides: { paper: float } },
    MuiDialogTitle: { styleOverrides: { root: { fontSize: "1.0625rem", fontWeight: 600, padding: "16px 20px 8px" } } },
    MuiDialogContent: { styleOverrides: { root: { padding: "8px 20px" } } },
    MuiDialogActions: { styleOverrides: { root: { padding: "12px 20px", gap: 8 } } },

    MuiMenu: {
      styleOverrides: {
        paper: float,
        list: { paddingTop: 4, paddingBottom: 4 },
      },
    },
    MuiMenuItem: {
      styleOverrides: {
        root: {
          minHeight: 32,
          fontSize: "0.8125rem",
          borderRadius: tokens.radius.small,
          "&.Mui-selected": {
            backgroundColor: t.action.selected,
            "&:hover": { backgroundColor: t.action.selected },
          },
        },
      },
    },

    MuiTooltip: {
      styleOverrides: {
        // MUI's tooltip is a grey slab that belongs to neither mode. This one is the app's own
        // raised surface, floating — a small piece of the app, above it.
        tooltip: {
          backgroundColor: t.surface.raised,
          color: t.text.primary,
          borderRadius: tokens.radius.small,
          fontSize: "0.75rem",
          padding: "4px 8px",
          ...float,
        },
        arrow: { color: t.surface.raised },
      },
    },

    MuiAlert: {
      styleOverrides: {
        // An Alert is a Paper, so it would inherit the extrusion. It is a tinted note *inside* a
        // panel, not a panel of its own: the tint is what separates it.
        root: { borderRadius: tokens.radius.medium, alignItems: "center", boxShadow: "none" },
        ...alertTints(t),
      },
    },

    // ---- what the rail is built from (P1T-161) --------------------------------------------

    MuiDrawer: {
      styleOverrides: {
        // A drawer has one edge that faces the app; the other three are the viewport. It is part of
        // the ground rather than a card on it, so it takes the hairline and no relief.
        paper: { border: 0, backgroundImage: "none", boxShadow: "none" },
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
