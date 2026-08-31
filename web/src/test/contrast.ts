// WCAG colour arithmetic, in one place.
//
// Written twice already — `theme/tokens.contrast.test.ts` (P1T-159) and `theme/components.test.tsx`
// (P1T-160), the second copied from the first with a comment saying why: importing a `*.test.ts`
// module re-registers its assertions inside the importing file's suite. That reason is real and it
// argues for *this* file rather than for a third copy, which is what P1T-163 needed. A plain module
// under `src/test/` has no suite to leak, the same way `agentSurface.ts` has none.
//
// Not under `src/theme/`: it is not part of the theme, it is what the specs measure the theme with.

/** `#RRGGBB` → the `rgb(r, g, b)` form jsdom and every browser report computed colours in. */
export function rgb(hex: string): string {
  const [r, g, b] = channels(hex);
  return `rgb(${r}, ${g}, ${b})`;
}

/** The three 8-bit channels of a `#RRGGBB` string. */
function channels(hex: string): [number, number, number] {
  const h = hex.replace("#", "");
  // Kept from the first copy of this arithmetic: a token that is not a 6-digit hex would otherwise
  // reach `parseInt` as `NaN` and fail the comparison for a reason nobody can read off the output.
  if (!/^[0-9a-fA-F]{6}$/.test(h)) throw new Error(`${hex} must be a 6-digit hex to be computable`);
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
  return [r, g, b];
}

/** sRGB channel → linear light. The 8-bit companding from WCAG's relative-luminance definition. */
function linear(value: number): number {
  const c = value / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

export function luminance(hex: string): number {
  const [r, g, b] = channels(hex).map(linear);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG contrast ratio, 1:1 … 21:1. */
export function contrastRatio(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/**
 * What a person actually sees when `fg` is drawn at `alpha` over `bg` — the composite an alpha
 * tint resolves to. Contrast is a property of that composite, never of the tint on its own.
 */
export function over(fg: string, alpha: number, bg: string): string {
  const [fr, fgc, fb] = channels(fg);
  const [br, bgc, bb] = channels(bg);
  const mix = (f: number, b: number) => Math.round(f * alpha + b * (1 - alpha));
  return `#${[mix(fr, br), mix(fgc, bgc), mix(fb, bb)]
    .map((v) => v.toString(16).padStart(2, "0"))
    .join("")}`;
}

/** `rgba(r, g, b, a)` — the form a computed style reports an alpha palette role in — resolved
 * over an opaque `#RRGGBB` background. The dock's user bubble is such a role (`action.selected`),
 * so what it costs in contrast is only answerable against the surface it lands on. */
export function rgbaOver(rgba: string, bg: string): string {
  const m = rgba.match(/rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+))?\s*\)/);
  if (!m) throw new Error(`not an rgb(a) colour: ${rgba}`);
  const [r, g, b] = [m[1], m[2], m[3]].map(Number);
  const alpha = m[4] === undefined ? 1 : Number(m[4]);
  const hex = `#${[r, g, b].map((v) => Math.round(v).toString(16).padStart(2, "0")).join("")}`;
  return over(hex, alpha, bg);
}
