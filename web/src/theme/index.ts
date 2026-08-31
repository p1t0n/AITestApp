// The two themes, built from one token object (`./tokens.ts`). This is the only file that knows a
// token exists: everything above it — 151 `sx` blocks and counting — reads MUI palette keys, so
// dark mode is free from here on and no component learns a second naming system.
//
// See `manuals/spa-design-system.md` §2 for why the tokens are expressed *through* MUI's
// vocabulary rather than exposed as a `theme.tokens.*` namespace of their own.
import { createTheme } from "@mui/material/styles";
import type { Theme } from "@mui/material/styles";
import { baselineStyles } from "./baseline";
import { tokens } from "./tokens";
import type { ThemeModeTokens } from "./tokens";
import type { ThemeMode } from "./mode";

/**
 * The one palette role MUI does not have. `raised` is the third surface step — a well *inside* a
 * panel — and `outline` is a control's own boundary, which has to clear 3:1 where `divider` must
 * not (see `tokens.ts`).
 *
 * Deliberately does not re-expose `page` / `surface`: those already have MUI names
 * (`background.default`, `background.paper`), and a second name for the same colour is exactly the
 * duplication this layer exists to avoid.
 */
export interface AppSurfacePalette {
  raised: string;
  outline: string;
}

declare module "@mui/material/styles" {
  interface Palette {
    surface: AppSurfacePalette;
  }
  interface PaletteOptions {
    surface: AppSurfacePalette;
  }
}

/**
 * The type scale. Dense on purpose: body copy is 14px and the headings stop well short of MUI's
 * defaults (`h1` is 6rem out of the box), because this app's pages are tables and forms, not
 * marketing. `textTransform: "none"` on buttons — a shouted label is not emphasis.
 */
function typography() {
  return {
    fontFamily: tokens.type.fontFamily,
    fontSize: tokens.type.baseSize,
    h1: { fontSize: "2rem", fontWeight: 700, lineHeight: 1.2, letterSpacing: "-0.02em" },
    h2: { fontSize: "1.625rem", fontWeight: 700, lineHeight: 1.25, letterSpacing: "-0.015em" },
    h3: { fontSize: "1.375rem", fontWeight: 700, lineHeight: 1.3, letterSpacing: "-0.01em" },
    h4: { fontSize: "1.25rem", fontWeight: 600, lineHeight: 1.3, letterSpacing: "-0.01em" },
    h5: { fontSize: "1.0625rem", fontWeight: 600, lineHeight: 1.35 },
    h6: { fontSize: "0.9375rem", fontWeight: 600, lineHeight: 1.4 },
    subtitle1: { fontSize: "0.875rem", fontWeight: 600, lineHeight: 1.45 },
    subtitle2: { fontSize: "0.8125rem", fontWeight: 600, lineHeight: 1.45 },
    body1: { fontSize: "0.875rem", lineHeight: 1.55 },
    body2: { fontSize: "0.8125rem", lineHeight: 1.5 },
    button: { fontSize: "0.8125rem", fontWeight: 600, textTransform: "none" as const },
    caption: { fontSize: "0.75rem", lineHeight: 1.45 },
    overline: {
      fontSize: "0.6875rem",
      fontWeight: 700,
      lineHeight: 1.6,
      letterSpacing: "0.08em",
      textTransform: "uppercase" as const,
    },
  };
}

function build(mode: ThemeMode, t: ThemeModeTokens): Theme {
  return createTheme({
    palette: {
      mode,
      primary: t.primary,
      error: t.error,
      warning: t.warning,
      info: t.info,
      success: t.success,
      background: { default: t.surface.page, paper: t.surface.surface },
      surface: { raised: t.surface.raised, outline: t.surface.outline },
      text: t.text,
      divider: t.divider,
      action: t.action,
    },
    shape: { borderRadius: tokens.radius },
    spacing: tokens.spacing,
    // The motion ceiling from §8 of the design record, at the one place MUI's components read a
    // duration from. `standard` is the longest thing the library will now animate.
    transitions: {
      duration: {
        shortest: 90,
        shorter: 120,
        short: tokens.motion.duration,
        standard: tokens.motion.duration,
        complex: tokens.motion.duration,
        enteringScreen: tokens.motion.duration,
        leavingScreen: tokens.motion.duration,
      },
      easing: { easeInOut: tokens.motion.easing },
    },
    typography: typography(),
    components: {
      MuiCssBaseline: { styleOverrides: baselineStyles(mode, t) },
    },
  });
}

/** Both themes, built once at module load — there are two of them and neither depends on state. */
export const lightTheme = build("light", tokens.modes.light);
export const darkTheme = build("dark", tokens.modes.dark);

export function themeFor(mode: ThemeMode): Theme {
  return mode === "dark" ? darkTheme : lightTheme;
}
