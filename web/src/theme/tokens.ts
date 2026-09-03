// The design tokens, declared once. Nothing in this file is read by a component: the tokens are
// *expressed through MUI palette keys* in `./index.ts`, so a component keeps writing
// `bgcolor: "background.paper"` / `borderColor: "divider"` / `color: "text.secondary"` and never
// learns a second naming system for the same idea. See `manuals/spa-design-system.md` §2.
//
// The look, in one line: a dense, dark-first product UI in **relief** — depth is a pair of shadows,
// one dark and one light, so a surface reads as lifted out of its ground or pressed into it; amber
// is the accent, and the only thing a hairline still does is mark a boundary a shadow cannot be
// measured at.
//
// This reverses the hairline-not-shadow rule this file was written under. The decision, and what it
// costs, is `manuals/adr-neumorphic-reskin.md`; the vocabulary is `CONTEXT.md` (**Relief**, **Relief
// Depth**, **Well**, **Float**, **Eyebrow**).

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
 * The three surfaces. `page` is the viewport, `surface` is a panel or a card, `raised` is a fill
 * *inside* a panel (a table head, an inline code span, a message bubble that is not a Well).
 *
 * `raised` is no longer a step on a ramp that carries depth: under Relief Depth, depth is the
 * shadow pair and it stops after two levels, so anything deeper than that is exactly this — a flat
 * fill, plus `divider` as a hairline. MUI's palette has only two of the three, which is why
 * `raised` is module-augmented onto `palette.surface`.
 */
export interface SurfaceTokens {
  page: string;
  surface: string;
  raised: string;
  /**
   * The boundary of a control — the one line that tells a person "this is an input". Held to 3:1
   * against all three surfaces (WCAG 1.4.11), which is *not* what a divider should be: see
   * `divider` below.
   *
   * It matters more under relief, not less. Neumorphism's known 1.4.11 failure is that a shadow
   * edge has nothing to measure — the prototype's own inputs are `border: 1px solid transparent` —
   * so this line stays underneath the relief as the boundary that is actually measurable.
   */
  outline: string;
}

/**
 * Relief: the shadow pair that carries depth, per mode, because the light half is white on a dark
 * ground and near-white on a grey one and does not exist at all on a white one.
 *
 * There are two sizes of each half and no third level. That is **Relief Depth**: one thing lifted,
 * one thing pressed into it, and nothing deeper — dual shadows at three levels turn to mud and
 * inset-inside-extruded-inside-inset has no physical reading. A component that wants a third level
 * gets `surface.raised` and `divider` instead, and this object has no third pair for it to reach.
 *
 * The geometry is the prototype's *ratios* at this app's density, not its absolute mass: the
 * artboards draw 8px/6px offsets against `size="medium"` components, and P1T-158 made `size="small"`
 * the default by deleting 109 explicit props. The offsets scale down to suit.
 */
export interface ReliefTokens {
  /** Level one, at rest: a panel, a card, a resting button. */
  extruded: string;
  /** Level one at chip/icon-button mass, where the full throw would swamp the element. */
  extrudedSmall: string;
  /** Level two: a Well — a search field, a message bubble, a pressed button. */
  inset: string;
  /** Level two at small mass. */
  insetSmall: string;
  /**
   * A surface genuinely above the page — a menu, a dialog, an autocomplete popup, the undocked
   * agent panel. The largest pair in the system; it is not a level of relief, it is what "above"
   * looks like. Replaces `overlayShadow`, whose claim to be the *only* shadow stopped being true.
   */
  float: string;
  /**
   * Float's second signal. Extrusion is ordinary now — everything at level one has it — so a
   * shadow alone no longer says "above the page". A `backdrop-filter` value, applied with `float`.
   */
  floatBackdrop: string;
}

export interface ModeTokens {
  surface: SurfaceTokens;
  text: { primary: string; secondary: string; disabled: string };
  /**
   * Decorative separation — a table rule, a card edge, a section break, and now the hairline under
   * a level-three flat fill. Deliberately below the 3:1 that `surface.outline` carries: a divider
   * is not what identifies the thing beside it, so 1.4.11 does not reach it, and a hairline at 3:1
   * reads as a heavy rule on every row of a dense table. The hairline look comes from being 1px,
   * not from being nearly invisible — hence a chosen floor of 1.4:1 (ours, not the standard's)
   * against all three surfaces, tested. Relief Depth makes that floor matter more than it did: a
   * third-level surface is *flat fill plus this*, with no shadow to fall back on.
   */
  divider: string;
  relief: ReliefTokens;
  /**
   * The amber step that clears 3:1 against every surface in this mode — the focus ring, and any
   * other place the accent has to be *measured* rather than filled.
   *
   * A separate token because the drawn accent cannot do this job in both modes: `#F59E0B` is 8:1
   * on the dark page and 1.9:1 on the light one. `primary.main` stays the amber the artboards draw
   * — it is a fill, labelled in ink — and this is the step that survives 1.4.11 on a grey ground.
   */
  focusRing: string;
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
 * Dark first. A blued near-black rather than pure `#000`: the dark half of every relief pair is
 * black at 45–60%, and black on black casts nothing.
 */
const dark: ModeTokens = {
  surface: {
    page: "#151A28",
    surface: "#1F2A3A",
    // Level three, and flat by rule — the fill under a table head or a code span.
    raised: "#28303E",
    outline: "#7C8899",
  },
  text: {
    primary: "#E8ECF4",
    secondary: "#9FAAB9",
    disabled: "#6B7688",
  },
  divider: "#434D5E",
  relief: {
    extruded: "6px 6px 9px rgba(0, 0, 0, 0.5), -6px -6px 9px rgba(255, 255, 255, 0.12)",
    extrudedSmall: "3px 3px 6px rgba(0, 0, 0, 0.45), -3px -3px 6px rgba(255, 255, 255, 0.10)",
    inset:
      "inset 4px 4px 6px rgba(0, 0, 0, 0.5), inset -4px -4px 6px rgba(255, 255, 255, 0.12)",
    insetSmall:
      "inset 2px 2px 4px rgba(0, 0, 0, 0.5), inset -2px -2px 4px rgba(255, 255, 255, 0.10)",
    float: "9px 9px 21px rgba(0, 0, 0, 0.6), -8px -8px 18px rgba(255, 255, 255, 0.10)",
    floatBackdrop: "blur(8px)",
  },
  focusRing: "#F59E0B",
  // The drawn accent, unchanged from the artboards in either mode. Its label is ink at 8:1 —
  // amber cannot take white anywhere, which is why `contrastText` is dark in light mode too.
  primary: { main: "#F59E0B", light: "#FBBF24", dark: "#B4700A", contrastText: "#151A28" },
  error: { main: "#FF6B6B", light: "#FF9E9E", dark: "#D64545", contrastText: "#1A0A0A" },
  // Re-hued off the accent: `#F5B942` sat half a step from `#F59E0B`, and two ambers meaning two
  // things is worse than a warning that sits nearer to error.
  warning: { main: "#F97316", light: "#FDBA74", dark: "#C2570A", contrastText: "#1A0E04" },
  // The prototype's one secondary hue. Blue beside amber reads as a re-skin that stopped halfway.
  info: { main: "#38B2AC", light: "#7BD3CE", dark: "#2C7A7B", contrastText: "#04140F" },
  success: { main: "#4ECB86", light: "#8BE0B0", dark: "#2E9E60", contrastText: "#04140B" },
  action: {
    hover: "rgba(232, 236, 244, 0.08)",
    selected: "rgba(245, 158, 11, 0.16)",
    focus: "rgba(245, 158, 11, 0.24)",
    disabled: "rgba(232, 236, 244, 0.30)",
    disabledBackground: "rgba(232, 236, 244, 0.12)",
  },
  chrome: {
    scrollbarThumb: "#3A4454",
    scrollbarThumbHover: "#4A5568",
    scrollbarTrack: "transparent",
    selectionBackground: "#7A4C05",
    selectionText: "#F4F7FB",
  },
};

/**
 * Light mode is an equal citizen: the same roles, re-tuned, never a washed-out dark. It has **no
 * white surface**, and that is load-bearing rather than a taste call — the light half of a
 * neumorphic pair is white, and white on `#FFFFFF` is invisible, which collapses relief to an
 * ordinary drop shadow. The CV sheet stays white and now reads as a whiter card floating on grey.
 */
const light: ModeTokens = {
  surface: {
    page: "#E5EAF3",
    surface: "#EEF1F8",
    raised: "#DCE3ED",
    outline: "#6E7684",
  },
  text: {
    primary: "#1B2331",
    // The prototype's own `#7A869A` is ~3.0:1 on `#EEF1F8` and fails its own artboards. The
    // artboards are the reference for the look, not for the numbers.
    secondary: "#535E70",
    disabled: "#8B96A8",
  },
  divider: "#B4BECD",
  relief: {
    extruded: "6px 6px 9px rgba(163, 177, 198, 0.6), -6px -6px 9px rgba(255, 255, 255, 0.95)",
    extrudedSmall:
      "3px 3px 6px rgba(163, 177, 198, 0.55), -3px -3px 6px rgba(255, 255, 255, 0.90)",
    inset:
      "inset 4px 4px 6px rgba(163, 177, 198, 0.6), inset -4px -4px 6px rgba(255, 255, 255, 0.95)",
    insetSmall:
      "inset 2px 2px 4px rgba(163, 177, 198, 0.55), inset -2px -2px 4px rgba(255, 255, 255, 0.90)",
    float: "9px 9px 21px rgba(163, 177, 198, 0.7), -8px -8px 18px rgba(255, 255, 255, 1)",
    floatBackdrop: "blur(8px)",
  },
  // `primary.main` at 1.9:1 on this ground cannot be a focus ring. This is the same amber, deep
  // enough to be measured against every surface — and it is `primary.dark`, not a sixth colour.
  focusRing: "#8A5406",
  // `dark` is the step that reads as *text* on a panel here: amber is a fill in light mode and a
  // label everywhere else. `light` is the brighter step a hairline or a hover wash is drawn from.
  primary: { main: "#F59E0B", light: "#FBBF24", dark: "#8A5406", contrastText: "#1B2331" },
  // `light` is the lighter *step* of the role, MUI's own meaning for it — not a pale tint. It has
  // to stay a fill that white text reads on, because that is what the roster-qa error bubble is
  // (`RosterQaTab.tsx`: `bgcolor: "error.light"` + `color: "error.contrastText"`).
  error: { main: "#C62828", light: "#CF3B3B", dark: "#8E1C1C", contrastText: "#FFFFFF" },
  warning: { main: "#9A4A0C", light: "#B85F17", dark: "#6E340A", contrastText: "#FFFFFF" },
  info: { main: "#2A7374", light: "#38949A", dark: "#1F5758", contrastText: "#FFFFFF" },
  success: { main: "#1B7A47", light: "#2A9159", dark: "#125934", contrastText: "#FFFFFF" },
  action: {
    hover: "rgba(27, 35, 49, 0.05)",
    selected: "rgba(245, 158, 11, 0.14)",
    focus: "rgba(245, 158, 11, 0.20)",
    disabled: "rgba(27, 35, 49, 0.30)",
    disabledBackground: "rgba(27, 35, 49, 0.10)",
  },
  chrome: {
    scrollbarThumb: "#B4BECD",
    scrollbarThumbHover: "#98A4B6",
    scrollbarTrack: "transparent",
    selectionBackground: "#FDE68A",
    selectionText: "#1B2331",
  },
};

/**
 * Shape, density and type — mode-independent, because a radius does not change with the lights.
 * `fontSize: 14` is the density lever: MUI scales its whole `rem` type scale off it, so one number
 * makes every label, cell and helper text compact at once.
 */
export const tokens = {
  /**
   * Three steps, replacing the old `8` / `6`. The prototype's 10 / 20 / 32 at this app's density:
   * `small` is what sits *inside* a control (a chip, a code span, the focus ring), `medium` is
   * MUI's `shape.borderRadius` and therefore what a Paper, a Button and a Menu get, and `large` is
   * for the few surfaces big enough to carry it — the brand tile, the agent panel.
   */
  radius: { small: 8, medium: 14, large: 24 },
  /** The unit MUI multiplies every `sx` spacing number by. */
  spacing: 8,
  /** Focus ring geometry, applied once in `MuiCssBaseline` — see §8 of the design record. */
  focusRing: { width: 2, offset: 2 },
  /**
   * Motion ceiling. Anything longer than this reads as the app being slow, not as polish. The
   * relief press (`translateY(2px)` plus swapping extruded for inset) rides inside it.
   */
  motion: { duration: 150, easing: "cubic-bezier(0.2, 0, 0.2, 1)" },
  type: {
    /**
     * Body. `DM Sans Variable` is the family `@fontsource-variable/dm-sans` registers; the rest is
     * the system stack, so the app is readable before — and if — the woff2 arrives.
     */
    fontFamily: ['"DM Sans Variable"', "system-ui", "-apple-system", "sans-serif"].join(", "),
    /** Headings, at 800. `@fontsource-variable/plus-jakarta-sans`, falling back to the body face. */
    fontFamilyHeading: [
      '"Plus Jakarta Sans Variable"',
      '"DM Sans Variable"',
      "system-ui",
      "sans-serif",
    ].join(", "),
    /**
     * Mono, promoted from "whatever the machine has" to a UI role: Eyebrows, table headers, tags
     * and `tnum` numerals are set in it, not just `code` and `pre`. A real family, self-hosted,
     * because an Eyebrow rendered in the browser's default monospace is the absence of a design.
     */
    fontFamilyMono: [
      '"JetBrains Mono Variable"',
      "ui-monospace",
      '"SFMono-Regular"',
      "Menlo",
      "monospace",
    ].join(", "),
    baseSize: 14,
  },
  modes: { light, dark },
} as const;

export type ThemeModeTokens = ModeTokens;
