// The CV Sheet's own theme — the **Light Lock**, as a thing rather than as a side effect.
//
// `CvPage` used to wrap the sheet in the app's live `lightTheme`, which made §7's "the sheet is
// frozen" true by luck: the sheet inherited whatever light mode happened to be that quarter. The
// neumorphic reversal (P1T-193) is what collected on that luck — light mode's paper became
// `#EEF1F8` and its accent became amber, so the next deploy would have handed clients a grey CV
// with amber section headings, and `CvSheet.lightLock.test.tsx` would have stayed green throughout
// because it asserted against the same tokens the app had just moved.
//
// So the colours here are **literals, and deliberately not derived from `tokens.ts`**. They are the
// palette the document was designed in, and they change when somebody decides the *document*
// changes — not when somebody decides the app's does. The spec asserts the same values written out
// by hand a second time; that duplication is the lock.
//
// What is *not* pinned, and why: the type. The families moved to DM Sans / Plus Jakarta in slice ①
// and Inter is no longer a dependency of this app, so "freeze the typeface" would mean re-adding a
// font package to hold a document to a face nobody can see any more. The artifact a client actually
// receives is the QuestPDF render (`CvPdfRenderer`), which owns its own fonts and never loads the
// SPA; this theme's job is that the *preview and the print path* stay the document's colours.
import { createTheme } from "@mui/material/styles";
import type { Theme } from "@mui/material/styles";
import { appTypography } from "./index";

/**
 * The document's palette. Every value is spelled out — there is no `tokens.modes.light` in this
 * file on purpose, and adding one would quietly undo the whole slice.
 */
const SHEET = {
  /** White paper. Under the app's grey light ground this now reads as a distinctly whiter card. */
  paper: "#FFFFFF",
  textPrimary: "#101418",
  textSecondary: "#566070",
  textDisabled: "#98A1AE",
  /** The blue the section headings were drawn in. The app's accent is amber; this one is not. */
  accent: "#2453D4",
  accentLight: "#5B82E8",
  accentDark: "#1A3EA3",
  onAccent: "#FFFFFF",
  divider: "#C5CBD4",
  /**
   * The app augments MUI's palette with `surface`, so this theme has to answer for it too. The
   * sheet draws neither a well nor a control, so these exist to be complete rather than to be used
   * — and they are the document's own greys, not the app's.
   */
  raised: "#EDEFF3",
  outline: "#7F8792",
  success: "#1B7A47",
  warning: "#96591A",
  error: "#C62828",
  info: "#0B6FA4",
} as const;

/**
 * The sheet's radius, also literal: the app's shape scale moved 8 → 14 in slice ①, and a document
 * is not a card in a product UI.
 */
const SHEET_RADIUS = 8;

/**
 * Built fresh rather than by extending `lightTheme`. Extending would inherit
 * `lightTheme.components`, whose overrides carry the app's *current* colours baked into them as
 * strings — the relief pair, the amber row wash, the mono pill tags — so the sheet would keep
 * tracking the palette through the back door while its own `palette` block looked pinned.
 */
export const cvSheetTheme: Theme = createTheme({
  palette: {
    mode: "light",
    background: { default: SHEET.paper, paper: SHEET.paper },
    text: {
      primary: SHEET.textPrimary,
      secondary: SHEET.textSecondary,
      disabled: SHEET.textDisabled,
    },
    primary: {
      main: SHEET.accent,
      light: SHEET.accentLight,
      dark: SHEET.accentDark,
      contrastText: SHEET.onAccent,
    },
    success: { main: SHEET.success, contrastText: SHEET.onAccent },
    warning: { main: SHEET.warning, contrastText: SHEET.onAccent },
    error: { main: SHEET.error, contrastText: SHEET.onAccent },
    info: { main: SHEET.info, contrastText: SHEET.onAccent },
    divider: SHEET.divider,
    surface: { raised: SHEET.raised, outline: SHEET.outline },
  },
  shape: { borderRadius: SHEET_RADIUS },
  // Type is the app's, for the reason in the header comment. Everything else about this theme is
  // the document's own.
  typography: appTypography(),
  components: {
    MuiPaper: {
      // No relief. The sheet is a document on a page, not a panel lifted off one — and it prints,
      // where a shadow is a grey smudge. MUI's own `elevation` shadow is what the sheet has always
      // had on screen and it stays; `CvPage`'s `sx` removes it at print media.
      styleOverrides: { root: { backgroundImage: "none" } },
    },
    MuiChip: {
      // The availability and skill chips. Small and rounded, as they were: the app's chips became
      // mono uppercase pills in slice ②, which is a product-UI voice rather than a document's.
      defaultProps: { size: "small" },
      styleOverrides: { root: { borderRadius: SHEET_RADIUS - 2 } },
    },
  },
});
