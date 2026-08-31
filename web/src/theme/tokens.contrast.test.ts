// The accessibility floors from `manuals/spa-design-system.md` §8, asserted against the token
// pairs rather than eyeballed on a screenshot. A screenshot check passes on the one screen it was
// taken of; this fails on the pair itself, in both modes, before anything renders.
//
// WCAG 2.1: 1.4.3 wants 4.5:1 for body text, 1.4.11 wants 3:1 for the visual boundary that
// identifies a control. `divider` is held to neither — see the block at the bottom for why that is
// a decision and not an omission.
import { describe, expect, it } from "vitest";
import { darkTheme, lightTheme } from "./index";
import { tokens } from "./tokens";
import type { ColorRole, ThemeModeTokens } from "./tokens";
// The WCAG arithmetic itself lives in `src/test/contrast.ts` (P1T-163) — three specs needed it.
import { contrastRatio } from "../test/contrast";

const modes: [string, ThemeModeTokens][] = [
  ["light", tokens.modes.light],
  ["dark", tokens.modes.dark],
];

/** The three surfaces anything can be drawn on, by the name a reader will recognise. */
function surfacesOf(t: ThemeModeTokens): [string, string][] {
  return [
    ["background.default", t.surface.page],
    ["background.paper", t.surface.surface],
    ["surface.raised", t.surface.raised],
  ];
}

function rolesOf(t: ThemeModeTokens): [string, ColorRole][] {
  return [
    ["primary", t.primary],
    ["error", t.error],
    ["warning", t.warning],
    ["info", t.info],
    ["success", t.success],
  ];
}

describe.each(modes)("%s mode contrast", (_mode, t) => {
  it.each(surfacesOf(t))("reads text at AA on %s", (_name, bg) => {
    // `text.disabled` is deliberately absent: it marks the *absence* of an affordance, and WCAG
    // 1.4.3 exempts inactive controls.
    expect(contrastRatio(t.text.primary, bg)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(t.text.secondary, bg)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(surfacesOf(t))("outlines a control and rings its focus at 3:1 on %s", (_name, bg) => {
    // The two things 1.4.11 actually reaches in this palette: the boundary that says "input", and
    // the focus ring, which is the accent.
    expect(contrastRatio(t.surface.outline, bg)).toBeGreaterThanOrEqual(3);
    expect(contrastRatio(t.primary.main, bg)).toBeGreaterThanOrEqual(3);
  });

  it.each(rolesOf(t))("labels a filled %s at AA, and reads as text on a panel", (_name, role) => {
    expect(contrastRatio(role.contrastText, role.main)).toBeGreaterThanOrEqual(4.5);
    // Semantic colours are text as often as they are fill: an Alert's icon, a form's helper line,
    // a Chip's label. `background.paper` is where all three of those sit.
    expect(contrastRatio(role.main, t.surface.surface)).toBeGreaterThanOrEqual(4.5);
  });

  it("labels the roster-qa error bubble, the one place a `light` step is a fill", () => {
    // `RosterQaTab.tsx`: `bgcolor: "error.light"` with `color: "error.contrastText"`. MUI computes
    // `contrastText` against `main` only, so this pairing is ours to hold — and it is the pairing
    // MUI's own defaults fail (white on `#ef5350` is 3.8:1).
    expect(contrastRatio(t.error.contrastText, t.error.light)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(surfacesOf(t))("keeps the divider a visible hairline on %s, not an outline", (_n, bg) => {
    const ratio = contrastRatio(t.divider, bg);
    // Our floor, not the standard's: a divider is decorative — the content beside it is what
    // identifies it — so 1.4.11 does not reach it. It still has to be seen.
    expect(ratio).toBeGreaterThanOrEqual(1.4);
    // And the ceiling is the point of having `surface.outline` at all. A divider promoted to 3:1
    // reads as a heavy rule on every row of a dense table, which is the look this design is not.
    expect(ratio).toBeLessThan(3);
  });
});

describe("the themes carry the tokens", () => {
  it("maps the surface ramp onto MUI's own names, plus the one role it lacks", () => {
    expect(lightTheme.palette.mode).toBe("light");
    expect(lightTheme.palette.background.default).toBe(tokens.modes.light.surface.page);
    expect(lightTheme.palette.background.paper).toBe(tokens.modes.light.surface.surface);
    expect(lightTheme.palette.surface.raised).toBe(tokens.modes.light.surface.raised);
    expect(lightTheme.palette.surface.outline).toBe(tokens.modes.light.surface.outline);

    expect(darkTheme.palette.mode).toBe("dark");
    expect(darkTheme.palette.background.default).toBe(tokens.modes.dark.surface.page);
    expect(darkTheme.palette.surface.raised).toBe(tokens.modes.dark.surface.raised);
  });

  it("keeps the declared contrastText instead of letting MUI guess one", () => {
    // `augmentColor` picks between two hardcoded values and would land on white over the bright
    // dark-mode accent at 3.2:1 — the trap `#2e5bff` was already in.
    expect(darkTheme.palette.primary.contrastText).toBe(tokens.modes.dark.primary.contrastText);
    expect(lightTheme.palette.primary.contrastText).toBe(tokens.modes.light.primary.contrastText);
  });

  it("caps every transition MUI emits at the motion ceiling", () => {
    for (const [name, duration] of Object.entries(lightTheme.transitions.duration)) {
      expect(duration, `transitions.duration.${name}`).toBeLessThanOrEqual(tokens.motion.duration);
    }
  });

  it("puts the focus ring in the baseline, so no component has to remember it", () => {
    const baseline = lightTheme.components?.MuiCssBaseline?.styleOverrides as Record<
      string,
      Record<string, unknown>
    >;
    expect(baseline["html *:focus-visible"].outline).toContain(tokens.modes.light.primary.main);
    expect(baseline[":root"].colorScheme).toBe("light");
    expect(
      (darkTheme.components?.MuiCssBaseline?.styleOverrides as Record<string, Record<string, unknown>>)[
        ":root"
      ].colorScheme,
    ).toBe("dark");
  });
});
