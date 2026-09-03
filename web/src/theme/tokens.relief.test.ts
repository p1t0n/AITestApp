// The claims the neumorphic reversal makes about the token layer, asserted rather than eyeballed
// against the artboards — `manuals/adr-neumorphic-reskin.md`, slice ① of P1T-193.
//
// `tokens.contrast.test.ts` next door holds the accessibility floors and is unchanged in what it
// claims. This file holds the *shape* of the new language: that depth is a pair of shadows and
// stops at two levels, that the accent is the drawn amber in both modes, that light mode has no
// white surface left to hide the light half of the pair, and that the three families are real
// dependencies rather than a CDN link.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { darkTheme, lightTheme } from "./index";
import { tokens } from "./tokens";
import type { ReliefTokens, ThemeModeTokens } from "./tokens";
import { luminance } from "../test/contrast";

const modes: [string, ThemeModeTokens][] = [
  ["light", tokens.modes.light],
  ["dark", tokens.modes.dark],
];

/** The two halves of a relief pair, split on the comma that separates the shadow layers. */
function layersOf(shadow: string): string[] {
  // Split on commas that are not inside `rgba(...)`.
  return shadow.split(/,(?![^(]*\))/).map((s) => s.trim());
}

/**
 * The largest absolute *offset* a shadow declares, in px — the relief's "throw". Only the first two
 * lengths of each layer are offsets; the third is the blur, which is not what the density argument
 * is about.
 */
function throwOf(shadow: string): number {
  const offsets = layersOf(shadow).flatMap((layer) =>
    [...layer.replace(/^inset /, "").matchAll(/(-?\d+(?:\.\d+)?)px/g)]
      .slice(0, 2)
      .map((m) => Math.abs(Number(m[1]))),
  );
  return Math.max(...offsets);
}

/** Every relief pair a mode declares, by the name a reader will recognise. */
function pairsOf(t: ThemeModeTokens): [string, string][] {
  return [
    ["extrudedSmall", t.relief.extrudedSmall],
    ["extruded", t.relief.extruded],
    ["insetSmall", t.relief.insetSmall],
    ["inset", t.relief.inset],
    ["float", t.relief.float],
  ];
}

describe.each(modes)("%s mode relief", (_mode, t) => {
  it.each(pairsOf(t))("casts %s as a pair — one dark half, one light half", (_name, shadow) => {
    const layers = layersOf(shadow);
    // A single shadow is a drop shadow, which is the look this reversal is not. Relief is two
    // shadows from opposite corners: that is the whole mechanism.
    expect(layers).toHaveLength(2);
    const [darkHalf, lightHalf] = layers;
    expect(darkHalf).toMatch(/^(inset )?\d/);
    // The light half is thrown from the opposite corner, so its offsets are negative.
    expect(lightHalf).toMatch(/-\d+px -\d+px/);
    expect(lightHalf).toMatch(/rgba\(\s*(255,\s*255,\s*255|163,\s*177,\s*198)/);
  });

  it.each([
    ["insetSmall", t.relief.insetSmall],
    ["inset", t.relief.inset],
  ])("presses %s into its parent, rather than lifting it", (_name, shadow) => {
    for (const layer of layersOf(shadow)) expect(layer.startsWith("inset ")).toBe(true);
  });

  it.each([
    ["extrudedSmall", t.relief.extrudedSmall],
    ["extruded", t.relief.extruded],
    ["float", t.relief.float],
  ])("lifts %s out of its ground, with no `inset` anywhere in it", (_name, shadow) => {
    expect(shadow).not.toContain("inset");
  });

  it("keeps the prototype's ratios at this app's density, not its absolute mass", () => {
    // The prototype draws `--ex`/`--in` at 8px/6px against `size="medium"` components. P1T-158
    // made `size="small"` the default and deleted 109 explicit props to get there, so the offsets
    // scale down — the same relief, at the mass the app actually has.
    expect(throwOf(t.relief.extruded)).toBeLessThan(8);
    expect(throwOf(t.relief.inset)).toBeLessThan(6);
    // Small is a smaller throw than full, and Float is the largest thing in the system.
    expect(throwOf(t.relief.extrudedSmall)).toBeLessThan(throwOf(t.relief.extruded));
    expect(throwOf(t.relief.insetSmall)).toBeLessThan(throwOf(t.relief.inset));
    expect(throwOf(t.relief.float)).toBeGreaterThan(throwOf(t.relief.extruded));
  });

  it("gives Float the backdrop that separates it from a merely extruded panel", () => {
    // Extrusion is ordinary now — everything at level one has it — so "above the page" needs a
    // second signal. See the **Float** entry in `CONTEXT.md`.
    expect(t.relief.floatBackdrop).toMatch(/blur\(\d+px\)/);
  });

  it("stops at two levels, so nothing can nest a third", () => {
    // Relief Depth: one thing lifted, one thing pressed into it, and no deeper — a third level is
    // a flat fill and a hairline, which is what `surface.raised` and `divider` are for. The token
    // object is where that ceiling is enforceable: there is no third pair to reach for.
    const names: (keyof ReliefTokens)[] = [
      "extruded",
      "extrudedSmall",
      "inset",
      "insetSmall",
      "float",
      "floatBackdrop",
    ];
    expect(Object.keys(t.relief).sort()).toEqual([...names].sort());
  });
});

describe("the palette the artboards drew", () => {
  it.each(modes)("keeps %s mode's accent the drawn amber, labelled in ink", (_mode, t) => {
    expect(t.primary.main).toBe("#F59E0B");
    // Amber cannot take white — this is the one role whose label is ink in *both* modes, which
    // reverses §2's "light mode's labels are white on a deep fill" for primary specifically.
    expect(luminance(t.primary.contrastText)).toBeLessThan(0.1);
  });

  it("re-hues `warning` off the accent, so two ambers never mean two things", () => {
    // Today's `#F5B942` sits half a step from `#F59E0B`. A warning nearer to error is the lesser
    // evil — `manuals/adr-neumorphic-reskin.md`.
    for (const [, t] of modes) expect(t.warning.main).not.toBe(t.primary.main);
    expect(tokens.modes.dark.warning.main).toBe("#F97316");
  });

  it("turns `info` teal, the prototype's one secondary hue", () => {
    expect(tokens.modes.dark.info.main).toBe("#38B2AC");
  });

  it("leaves light mode no white surface for the light half of the pair to vanish on", () => {
    // Neumorphic light shadows are grey-plus-white. On `#FFFFFF` the white half is invisible and
    // the look collapses to an ordinary drop shadow, so the ground goes grey and stays grey.
    const { page, surface, raised } = tokens.modes.light.surface;
    for (const colour of [page, surface, raised]) {
      expect(colour.toUpperCase()).not.toBe("#FFFFFF");
      expect(luminance(colour)).toBeLessThan(0.9);
    }
  });
});

describe("shape and type", () => {
  it("carries three radius steps, the prototype's 10/20/32 at this app's density", () => {
    expect(tokens.radius.small).toBeLessThan(tokens.radius.medium);
    expect(tokens.radius.medium).toBeLessThan(tokens.radius.large);
    expect(tokens.radius.large).toBeLessThan(32);
    // MUI's `shape.borderRadius` is the default a Paper, a Button and a Menu all read: the middle
    // step, not the smallest, or every card in the app goes back to looking like a chip.
    expect(lightTheme.shape.borderRadius).toBe(tokens.radius.medium);
  });

  it("gives headings, body and mono three distinct families", () => {
    const { fontFamily, fontFamilyHeading, fontFamilyMono } = tokens.type;
    expect(fontFamilyHeading).toContain("Plus Jakarta Sans Variable");
    expect(fontFamily).toContain("DM Sans Variable");
    expect(fontFamilyMono).toContain("JetBrains Mono Variable");
    expect(new Set([fontFamily, fontFamilyHeading, fontFamilyMono]).size).toBe(3);
  });

  it("promotes mono from a system stack to a real role", () => {
    // It used to be `SFMono-Regular, Menlo, Consolas…` — whatever the machine had. An Eyebrow
    // rendered in the browser's default monospace is not the design, it is the absence of one.
    expect(tokens.type.fontFamilyMono.startsWith('"JetBrains Mono Variable"')).toBe(true);
  });

  it("sets Eyebrows in mono, which is what makes mono a UI role and not a code font", () => {
    expect(lightTheme.typography.overline.fontFamily).toBe(tokens.type.fontFamilyMono);
    expect(lightTheme.typography.overline.textTransform).toBe("uppercase");
    expect(darkTheme.typography.overline.fontFamily).toBe(tokens.type.fontFamilyMono);
  });

  it("sets headings in the heading family at the weight the artboards draw", () => {
    expect(lightTheme.typography.h1.fontFamily).toBe(tokens.type.fontFamilyHeading);
    expect(lightTheme.typography.h1.fontWeight).toBe(800);
  });

  it("self-hosts all three, the way the app has self-hosted its font since P1T-159", () => {
    // §3's rule, unchanged by the reversal: an authenticated internal tool does not fetch fonts
    // from a third-party CDN. A family named in the tokens and served from Google is the same bug
    // as a family named in the tokens and served from nowhere.
    // `process.cwd()` is `web/` under vitest; `import.meta.url` is not a `file:` URL there.
    const pkg = JSON.parse(readFileSync(resolve(process.cwd(), "package.json"), "utf8"));
    const main = readFileSync(resolve(process.cwd(), "src/main.tsx"), "utf8");
    for (const family of ["plus-jakarta-sans", "dm-sans", "jetbrains-mono"]) {
      expect(pkg.dependencies[`@fontsource-variable/${family}`]).toBeTruthy();
      expect(main).toContain(`@fontsource-variable/${family}`);
    }
  });
});
