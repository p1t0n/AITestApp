// The accessibility and chrome floors, applied once through `MuiCssBaseline`. They live here
// rather than in 24 components because they are trivial to put in the foundation and expensive to
// retrofit — `manuals/spa-design-system.md` §8.
//
// Component overrides (`MuiButton`, `MuiPaper`, …) are *not* here: they are P1T-160's file,
// `src/theme/components.ts`. This is only what has no component to hang off.
import type { CSSObject } from "@mui/material";
import { tokens } from "./tokens";
import type { ThemeModeTokens } from "./tokens";
import type { ThemeMode } from "./mode";

export function baselineStyles(mode: ThemeMode, t: ThemeModeTokens): CSSObject {
  return {
    // Tells the browser which way its own furniture goes: native scrollbars, form controls, the
    // canvas behind the app. Without it a dark app keeps a white overscroll area on iOS.
    ":root": { colorScheme: mode },

    html: {
      // Firefox's scrollbar API. WebKit's is the pseudo-element block below.
      scrollbarColor: `${t.chrome.scrollbarThumb} ${t.chrome.scrollbarTrack}`,
      scrollbarWidth: "thin",
    },

    body: {
      WebkitFontSmoothing: "antialiased",
      MozOsxFontSmoothing: "grayscale",
    },

    // `fontFamilyMono` is a real family now, not a system stack, and it reaches further than the
    // four elements below — Eyebrows, table headers and tags are set in it through the theme. What
    // stays here is the document-level floor: nothing owns `code`, so nothing else can style it.
    // `tabular-nums` rides along, because the numerals are half the reason mono was promoted.
    "code, kbd, samp, pre": {
      fontFamily: tokens.type.fontFamilyMono,
      fontVariantNumeric: "tabular-nums",
    },

    // One visible focus ring for every interactive element, whatever renders it. Applied to
    // `:focus-visible` rather than `:focus` so a mouse click does not leave a ring behind, and
    // written as a global rule because the alternative is remembering it per component — and the
    // failure mode of forgetting is an element a keyboard user cannot locate.
    "html *:focus-visible": {
      outline: `${tokens.focusRing.width}px solid ${t.focusRing}`,
      outlineOffset: tokens.focusRing.offset,
    },

    "::selection": {
      backgroundColor: t.chrome.selectionBackground,
      color: t.chrome.selectionText,
    },

    // WebKit/Blink scrollbars. The thumb is drawn inside a transparent border so it reads as a
    // floating pill rather than a full-height gutter.
    "*::-webkit-scrollbar": { width: 10, height: 10 },
    "*::-webkit-scrollbar-track": { backgroundColor: t.chrome.scrollbarTrack },
    "*::-webkit-scrollbar-thumb": {
      backgroundColor: t.chrome.scrollbarThumb,
      borderRadius: tokens.radius.small,
      border: "2px solid transparent",
      backgroundClip: "content-box",
    },
    "*::-webkit-scrollbar-thumb:hover": { backgroundColor: t.chrome.scrollbarThumbHover },
    "*::-webkit-scrollbar-corner": { backgroundColor: "transparent" },

    // Relief does not print. A dual shadow on paper is a grey smudge around every card, and the
    // CV path proves people print from this app. The rail and the dock hide themselves at print
    // media; page content does not, and every panel is extruded now — so the floor is global and
    // lives here with the others. `!important` because it has to beat a component's own override
    // and an `sx` block alike: this is the one place that outranks the look.
    "@media print": {
      "*, *::before, *::after": { boxShadow: "none !important" },
    },

    // Motion is off, not shortened, when the OS asks. The ≤150ms *ceiling* is not enforced here —
    // it is `theme.transitions.duration`, which is where MUI's components read their timings from,
    // so capping it there binds every transition the library emits rather than only the ones a
    // global selector happens to reach.
    "@media (prefers-reduced-motion: reduce)": {
      "*, *::before, *::after": {
        animationDuration: "0.01ms !important",
        animationIterationCount: "1 !important",
        transitionDuration: "0.01ms !important",
        scrollBehavior: "auto !important",
      },
    },
  };
}
