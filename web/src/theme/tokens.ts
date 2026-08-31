// The design tokens, declared once. Nothing in this file is read by a component: the tokens are
// *expressed through MUI palette keys* in `./index.ts`, so a component keeps writing
// `bgcolor: "background.paper"` / `borderColor: "divider"` / `color: "text.secondary"` and never
// learns a second naming system for the same idea. See `manuals/spa-design-system.md` §2.
//
// The look, in one line: a dense, dark-first product UI — hairline borders instead of shadows,
// accent reserved for the primary action and the focus ring.

/** A colour role that MUI's palette can express as `main`/`light`/`dark` + its own text colour. */
export interface ColorRole {
  main: string;
  light: string;
  dark: string;
  /**
   * Text placed on `main`. Declared rather than left to MUI's `augmentColor`, which only ever picks
   * between two hardcoded values and would land on white over this palette's brighter dark-mode
   * fills at 3.2:1.
   *
   * MUI's contract is `contrastText` on `main` and nothing else, and `tokens.contrast.test.ts`
   * asserts exactly that — plus one extra pairing, `error.light`, because the roster-qa error
   * bubble is the app's only place that fills with a `light` step and labels it with
   * `contrastText`.
   */
  contrastText: string;
}

/**
 * The three-step surface ramp. `page` is the viewport, `surface` is a panel or a card, `raised` is
 * a well *inside* a panel (an inline code span, a message bubble, a table head). MUI's palette has
 * only two of the three, which is why `raised` is module-augmented onto `palette.surface`.
 */
export interface SurfaceTokens {
  page: string;
  surface: string;
  raised: string;
  /**
   * The boundary of a control — the one line that tells a person "this is an input". Held to 3:1
   * against all three surfaces (WCAG 1.4.11), which is *not* what a divider should be: see
   * `divider` below.
   */
  outline: string;
}

export interface ModeTokens {
  surface: SurfaceTokens;
  text: { primary: string; secondary: string; disabled: string };
  /**
   * Decorative separation — a table rule, a card edge, a section break. Deliberately below the 3:1
   * that `surface.outline` carries: a divider is not what identifies the thing beside it, so
   * 1.4.11 does not reach it, and a hairline at 3:1 reads as a heavy rule on every row of a dense
   * table. The hairline look comes from being 1px, not from being nearly invisible — hence a
   * chosen floor of 1.4:1 (ours, not the standard's) against all three surfaces, tested.
   */
  divider: string;
  /**
   * The one shadow in the system. Separation is a hairline border everywhere *except* a surface
   * that genuinely floats over another — a menu, a dialog, an autocomplete popup, the undocked
   * agent panel. Those cannot be separated by a border, because a border does not say "above".
   * Anything that is merely *next to* something else gets `divider`, not this.
   */
  overlayShadow: string;
  primary: ColorRole;
  error: ColorRole;
  warning: ColorRole;
  info: ColorRole;
  success: ColorRole;
  /** Interaction washes. Alpha values, because they have to sit over any of the three surfaces. */
  action: {
    hover: string;
    selected: string;
    focus: string;
    disabled: string;
    disabledBackground: string;
  };
  /** Chrome that is not a component: the scrollbar and the selection highlight. */
  chrome: {
    scrollbarThumb: string;
    scrollbarThumbHover: string;
    scrollbarTrack: string;
    selectionBackground: string;
    selectionText: string;
  };
}

/**
 * Dark first. The ramp is a cool near-black rather than pure `#000` so that a raised well can be
 * *lighter* than its panel without reaching grey — a shadow cannot separate anything on black.
 */
const dark: ModeTokens = {
  surface: {
    page: "#0E1116",
    surface: "#151A21",
    raised: "#1E242E",
    outline: "#6D7887",
  },
  text: {
    primary: "#E6EDF3",
    secondary: "#9BA7B4",
    disabled: "#616C79",
  },
  divider: "#3A4451",
  // Near-black cannot be shadowed by a darker black, so dark mode's overlay shadow is deep and
  // wide rather than tight — it reads as depth-of-field, not as a drop shadow.
  overlayShadow: "0 12px 32px rgba(0, 0, 0, 0.64)",
  // A brighter blue than light mode's: the accent has to carry 3:1 as a focus ring against a
  // near-black page, and a deep blue cannot. Its label is ink, not white — white on this blue is
  // 3.2:1, which is the trap `#2e5bff` fell into on dark.
  primary: { main: "#5B8CFF", light: "#8FB0FF", dark: "#3C6BE0", contrastText: "#0A0E14" },
  error: { main: "#FF6B6B", light: "#FF9E9E", dark: "#D64545", contrastText: "#1A0A0A" },
  warning: { main: "#F5B942", light: "#FFD37A", dark: "#C98F1E", contrastText: "#1A1206" },
  info: { main: "#5BB8FF", light: "#96D3FF", dark: "#2E8FD6", contrastText: "#04121A" },
  success: { main: "#4ECB86", light: "#8BE0B0", dark: "#2E9E60", contrastText: "#04140B" },
  action: {
    hover: "rgba(230, 237, 243, 0.08)",
    selected: "rgba(91, 140, 255, 0.16)",
    focus: "rgba(91, 140, 255, 0.24)",
    disabled: "rgba(230, 237, 243, 0.30)",
    disabledBackground: "rgba(230, 237, 243, 0.12)",
  },
  chrome: {
    scrollbarThumb: "#39424E",
    scrollbarThumbHover: "#4B5665",
    scrollbarTrack: "transparent",
    selectionBackground: "#2E4C99",
    selectionText: "#F2F6FB",
  },
};

/** Light mode is an equal citizen: the same roles, re-tuned, never a washed-out dark. */
const light: ModeTokens = {
  surface: {
    page: "#F5F6F8",
    surface: "#FFFFFF",
    raised: "#EDEFF3",
    outline: "#7F8792",
  },
  text: {
    primary: "#101418",
    secondary: "#566070",
    disabled: "#98A1AE",
  },
  divider: "#C5CBD4",
  overlayShadow: "0 12px 28px rgba(16, 20, 24, 0.16)",
  // Deeper and calmer than the `#2e5bff` this replaces, which was loud in light mode and unusable
  // in dark. White reads on it at 6.4:1, so the primary button keeps a white label.
  primary: { main: "#2453D4", light: "#5B82E8", dark: "#1A3EA3", contrastText: "#FFFFFF" },
  // `light` is the lighter *step* of the role, MUI's own meaning for it — not a pale tint. It has
  // to stay a fill that white text reads on, because that is what the roster-qa error bubble is
  // (`RosterQaTab.tsx`: `bgcolor: "error.light"` + `color: "error.contrastText"`).
  error: { main: "#C62828", light: "#CF3B3B", dark: "#8E1C1C", contrastText: "#FFFFFF" },
  warning: { main: "#96591A", light: "#B87A2A", dark: "#6E4013", contrastText: "#FFFFFF" },
  info: { main: "#0B6FA4", light: "#1785BE", dark: "#08527A", contrastText: "#FFFFFF" },
  success: { main: "#1B7A47", light: "#2A9159", dark: "#125934", contrastText: "#FFFFFF" },
  action: {
    hover: "rgba(16, 20, 24, 0.05)",
    selected: "rgba(36, 83, 212, 0.10)",
    focus: "rgba(36, 83, 212, 0.16)",
    disabled: "rgba(16, 20, 24, 0.30)",
    disabledBackground: "rgba(16, 20, 24, 0.10)",
  },
  chrome: {
    scrollbarThumb: "#C3C9D2",
    scrollbarThumbHover: "#A7AFBB",
    scrollbarTrack: "transparent",
    selectionBackground: "#C7D8FA",
    selectionText: "#0A1020",
  },
};

/**
 * Shape, density and type — mode-independent, because a radius does not change with the lights.
 * `fontSize: 14` is the density lever: MUI scales its whole `rem` type scale off it, so one number
 * makes every label, cell and helper text compact at once.
 */
export const tokens = {
  radius: 8,
  /** Small radius for the things that sit *inside* a control: chips, code spans, the focus ring. */
  radiusSmall: 6,
  /** The unit MUI multiplies every `sx` spacing number by. */
  spacing: 8,
  /** Focus ring geometry, applied once in `MuiCssBaseline` — see §8 of the design record. */
  focusRing: { width: 2, offset: 2 },
  /** Motion ceiling. Anything longer than this reads as the app being slow, not as polish. */
  motion: { duration: 150, easing: "cubic-bezier(0.2, 0, 0.2, 1)" },
  type: {
    /**
     * `Inter Variable` is the family `@fontsource-variable/inter` registers. The rest is the system
     * stack, so the app is readable before — and if — the woff2 arrives.
     */
    fontFamily: [
      '"Inter Variable"',
      "-apple-system",
      "BlinkMacSystemFont",
      '"Segoe UI"',
      "Roboto",
      '"Helvetica Neue"',
      "Arial",
      "sans-serif",
    ].join(", "),
    fontFamilyMono: ['"SFMono-Regular"', "Menlo", "Consolas", '"Liberation Mono"', "monospace"].join(
      ", ",
    ),
    baseSize: 14,
  },
  modes: { light, dark },
} as const;

export type ThemeModeTokens = ModeTokens;
