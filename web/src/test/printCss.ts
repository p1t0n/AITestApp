// Reading the print CSS a component emits, in jsdom.
//
// This is the *lower* of the project's two print checks and knows it. jsdom implements no cascade
// and never re-evaluates a media query, so `getComputedStyle` there reports declared values, not
// resolved ones — a rule that emits perfectly and loses a specificity tie reads identical to one
// that wins (P1T-159's focus ring, which emitted flawless CSS and rendered nothing). What these
// helpers do prove is cheap and still worth having: the declaration exists, and it is attached to
// the element's own generated class rather than to a selector nobody owns — the coupling P1T-154
// removed from `index.css`.
//
// Whether the rule *wins* is a browser question, and `web/e2e/print.e2e.ts` plus
// `web/e2e/cv-print.e2e.ts` answer it at real print media. These live here so the default
// `npm test` run still fails when a print rule is deleted: the e2e suite needs a database and a
// Chromium and does not run by default, and a rule guarded only by the run that never happens is
// not guarded (P1T-143).
//
// Extracted from `CvPage.print.test.tsx` when the agent dock grew print rules of its own
// (P1T-166) — two copies of a brace matcher is one too many.

/** Everything emotion + CssBaseline have injected into the document so far, whitespace stripped. */
export function emittedCss(): string {
  return Array.from(document.querySelectorAll("style"))
    .map((s) => s.textContent ?? "")
    .join("\n")
    .replace(/\s+/g, "");
}

/** The `@media print{…}` blocks of the emitted CSS, brace-matched so nested rules stay whole. */
export function printBlocks(): string[] {
  const css = emittedCss();
  const blocks: string[] = [];
  const opener = /@mediaprint\{/g;
  let match: RegExpExecArray | null;
  while ((match = opener.exec(css)) !== null) {
    let depth = 1;
    let i = match.index + match[0].length;
    for (; i < css.length && depth > 0; i++) {
      if (css[i] === "{") depth++;
      else if (css[i] === "}") depth--;
    }
    blocks.push(css.slice(match.index, i));
  }
  return blocks;
}

/**
 * Every print block carrying one of this element's own generated classes, concatenated in emitted
 * order — `undefined` when it has none.
 *
 * All of them, not the first: an element can be the subject of more than one `@media print` block
 * and in this app one already is. `MuiFab` ships its own print rule (`print-color-adjust: exact`,
 * MUI insisting the bubble keep its background colour on paper), so the dock's bubble emits two
 * blocks against the same class and a first-match reader answered MUI's — which is how this
 * function was found to be wrong (P1T-166). Concatenating is right for what callers do with the
 * result, which is ask whether a declaration is anywhere in the element's print styling.
 *
 * It does not, and cannot, say which of two competing declarations wins. That is the browser's
 * job; see the module comment.
 */
export function printBlockFor(el: Element): string | undefined {
  const classes = Array.from(el.classList).filter((c) => c.startsWith("css-"));
  const blocks = printBlocks().filter((b) => classes.some((c) => b.includes(`.${c}`)));
  return blocks.length ? blocks.join("") : undefined;
}
