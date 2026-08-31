# SPA design system

> **Status (2026-08-31):** **slice 1 built** (P1T-159 — §2, §3's type/accent rules, §8). Slices 2–6
> are still the agreed target, not the code. Tracked as **P1T-158** with six children, P1T-159 …
> P1T-164, plus P1T-165 deferred. Each section names the slice that makes it true. Update the
> status line as slices land — a rule here that no code enforces is a lie, not a plan.

The goal in one sentence: a dense, dark-first product UI — hairline borders instead of shadows,
accent reserved for the primary action and the focus ring, compact rows — with light mode as an
equal citizen, not an afterthought.

## 1. The library stays

MUI 5.18 keeps every pixel. A custom look is 90% tokens and component overrides and about 10%
library identity, so the alternatives were priced and rejected:

- **Tailwind + shadcn/Radix**: full control, and a rewrite of all 24 components, 25 `data-testid`
  hooks, every dialog and the e2e suite. Weeks of risk to the agent dock and the passkey journey,
  bought with aesthetics.
- **MUI 7 for stable `cssVariables: true`**: a major upgrade whose payoff (no re-render on toggle,
  no SSR flash) buys nothing in an SPA with no server render.
- **MUI 5's experimental `CssVarsProvider`**: the same non-payoff, on an experimental API.
- **Hybrid (Tailwind shell, MUI pages)**: two systems, two vocabularies, permanent seam. Worst.

## 2. Tokens speak MUI's vocabulary

Tokens are defined once in `src/theme/tokens.ts` and are then **expressed through MUI palette
keys**. Components keep writing `bgcolor: "background.paper"`, `borderColor: "divider"`,
`color: "text.secondary"` and never import a token. Where MUI's palette lacks a role we need — the
third surface step — we module-augment the palette (`surface.raised`) rather than invent a parallel
namespace.

**Why:** the app has exactly four hardcoded colour sites today (`main.tsx`, `App.tsx` `grey.50`,
`AgentMarkdown` and `RosterQaTab` `grey.100`). Fixing those four and routing everything else through
palette keys means dark mode is free forever and no component ever learns a second naming system.
A `theme.tokens.*` namespace read directly by components would have been that second system.

Two themes are built from one token object and swapped by `ThemeProvider` at the root. Mode default
is the system preference; a user override persists in `localStorage` behind the same hand-rolled
subscription used by `src/auth/session.ts` (§3 of the architecture record) — **no React Context is
added to this app.**

**Built (P1T-159).** `src/theme/tokens.ts` holds the tokens, `src/theme/index.ts` builds
`lightTheme` and `darkTheme` from them, `src/theme/mode.ts` is the Theme Mode store and
`src/theme/baseline.ts` the floors. `main.tsx` picks a theme with `useThemeMode()`. The four
hardcoded colour sites are gone: `App.tsx` → `background.default`, `AgentMarkdown` and
`RosterQaTab` → `surface.raised`, and the hex in `main.tsx` left with the four-line theme.

Two things the palette needed that the plan had not priced:

- **`surface.outline` as well as `surface.raised`.** One `divider` token cannot do both jobs.
  1.4.11 wants 3:1 for the boundary that *identifies* a control, and a divider pushed to 3:1 reads
  as a heavy rule on every row of a dense table — in light mode it lands around `#8B9199`, which is
  not a hairline by any reading. So `divider` is a decorative hairline held to a chosen floor of
  1.4:1 (ours, not the standard's), and `surface.outline` is the control boundary held to 3:1. Both
  are asserted, in both modes, against all three surfaces. Note the *consumer* of
  `surface.outline` is slice 2's `MuiOutlinedInput` override — MUI's own default notched outline is
  `rgba(255,255,255,0.23)`, about 2.1:1, so **inputs do not meet 3:1 until P1T-160 lands.** The
  token is ready and tested; nothing points at it yet.
- **`contrastText` is declared per role, not computed.** `augmentColor` picks between two hardcoded
  values and would put white on the bright dark-mode accent at 3.2:1 — the exact trap `#2e5bff`
  was in. Dark mode's labels are ink on a bright fill; light mode's are white on a deep one. In
  light mode `light` stays a mid *step* rather than a pale tint, because the roster-qa error bubble
  fills with `error.light` and labels it with `error.contrastText` (MUI's own default fails that
  pairing at 3.8:1).

The floors are asserted against the token pairs in `web/src/theme/tokens.contrast.test.ts` — 34
assertions, both modes — rather than eyeballed on a screenshot, which only ever proves the screen
it was taken of.

*Slice 1 — P1T-159.*

## 3. The look rules

- **Accent**: a deeper, calmer blue replaces `#2e5bff`, which is loud on a dark surface. It appears
  on the primary action and the focus ring, and essentially nowhere else.
- **Surfaces**: a three-step ramp (page → surface → raised). Separation is a hairline border;
  shadows are the exception, not the mechanism. `MuiPaper` defaults to `elevation: 0` and
  `variant: "outlined"`.
- **Density**: `size: "small"` is the default prop for inputs and buttons; table rows are compact
  with hairline rules. Radius 8.
- **Type**: Inter variable via `@fontsource-variable/inter`, system stack as fallback. Self-hosted
  and versioned like any dependency — an authenticated internal tool does not fetch fonts from a
  third-party CDN.

**The override policy, which is the load-bearing rule:** if a look is needed twice, it belongs in
`src/theme/components.ts`, not in a third `sx`. The app has 151 `sx` blocks today; they are
overwhelmingly spacing and layout, and they should stay that way. Overrides cover `MuiButton`,
`MuiTextField`/`MuiOutlinedInput`, `MuiPaper`, `MuiTable`/`MuiTableCell`, `MuiDialog`, `MuiChip`,
`MuiMenu`/`MuiMenuItem`, `MuiDrawer`, `MuiListItemButton`, `MuiTooltip`, `MuiAlert` and
`MuiCssBaseline`. There is no `MuiTabs` override because the app renders no tabs anywhere —
the dock's surface picker is a grouped `Menu` (P1T-152).

**Built in slice 1:** the accent (`#2453D4` light / `#5B8CFF` dark), the three-step ramp, radius 8,
Inter, and the type scale — body copy at 14px, headings well short of MUI's defaults (`h1` is 6rem
out of the box), `textTransform: "none"` on buttons. **Not** built: `size: "small"` defaults and the
table density, which are component overrides and therefore slice 2.

*Slices 1–2 — P1T-159, P1T-160.*

## 4. The shell: two edges that push

The `AppBar` becomes a left rail: 240px, collapsible to 64px, collapse state persisted. Collapsed
items keep an `aria-label` and grow a tooltip — the e2e suite asserts by accessible name, so an
icon without a name is a broken test, not a style choice. Below `md` the rail becomes a temporary
`Drawer` behind a slim top bar. The rail's bottom block holds the theme toggle, the user, and
`Sign out`.

The rail publishes its width as a CSS custom property mirroring `DOCK_PUSH_VAR`, so the root element
makes room for both edges by the same mechanism: **the shell makes room for whatever an edge
publishes it is covering, and neither edge participates in layout.** That symmetry is the point —
the dock's existing push contract is not modified, it is copied.

*Slice 3 — P1T-161.*

## 5. Page headers and width

Every routed page gets one `PageHeader`: title, optional back/breadcrumb, a right slot for primary
actions, sticky with a border that appears on scroll. Pages move their existing inline heading strip
into it and keep their internals.

Width is decided per page rather than globally, because `Container maxWidth="lg"` centred inside the
remaining column reads wrong once a rail eats 240px and a dock can eat more: tables (roster,
catalog, users) go full-bleed to a ~1440px cap; forms, the auth pages and the CV sheet stay capped.
`PageHeader` takes the width as a prop so the choice is visible at the top of each page.

*Slice 4 — P1T-162.*

## 6. The dock

The dock adopts the tokens and gets a chrome refresh — header row, picker button, a resize handle
that shows itself on hover, message bubbles retuned for dark. Its information architecture does not
change: the grouped picker and the nine Agent Surfaces are P1T-152's shape and stay.

*Slice 5 — P1T-163.*

## 7. The CV sheet is frozen, and always light

The CV sheet's visual design does not change. More than that: `CvPage` renders its sheet under a
nested **light** `ThemeProvider` regardless of the app's mode.

**Why:** the sheet is a client-facing artifact and the print path only forces `body` white, so a
dark-mode `Paper` would print — or preview — as something no client should see. A nested provider is
one line at one boundary; the alternative is a growing pile of `@media print` colour overrides that
must be kept exhaustive forever. What you see is what prints, for every user, in either mode.

*Slice 6 — P1T-164.*

## 8. Floors that are cheaper now than later

- **Contrast**: AA in *both* modes — 4.5:1 text, 3:1 UI. Checked against the token pairs, not
  eyeballed on a screenshot.
- **Focus**: a visible `:focus-visible` ring, 2px accent with offset, on every interactive element.
- **Motion**: transitions ≤150ms, on `opacity`/`transform`/`colour` only, and
  `prefers-reduced-motion` switches them off.

All four live in `MuiCssBaseline` (`src/theme/baseline.ts`), plus the scrollbar and selection
colours. They are trivial to put in the foundation and expensive to retrofit across 24 components.

**Built (P1T-159), with two things learned by measuring it in a real browser:**

- The focus ring is `html *:focus-visible`, not `*:focus-visible`. The plain universal selector is
  specificity (0,1,0) — the same as one emotion class — so `MuiButtonBase`'s own `outline: 0`, being
  injected later, won on source order and the ring silently did not render. `html *…` is (0,1,1) and
  wins without an `!important`. This was caught by driving Chromium and reading
  `getComputedStyle`, not by reading the CSS: the `outline-offset` was applying while the `outline`
  was not, which is invisible in a diff.
- Text inputs keep MUI's own indicator rather than the global ring. `MuiInputBase`'s
  `&:focus { outline: 0 }` is (0,2,0) and beats the global rule, but the notched outline goes 2px
  `primary.main` on focus, so focus is visible — in the same accent, by a different mechanism.
  Fighting the specificity would have bought a doubled ring, not an accessible one.
- The ≤150ms ceiling is **not** a CSS rule: it is `theme.transitions.duration`, capped at every key,
  which is where MUI's components read their timings from. A global selector would only reach the
  transitions it happens to match. `prefers-reduced-motion` is still the CSS kill switch.

## 9. What must not move

| Frozen | Why |
|---|---|
| 25 `data-testid` hooks | The unit suite's grip on the DOM; renaming one is a silent test deletion |
| Accessible names `Sign out`, `CVs`, `Sign in` | The e2e suite asserts by role + name (`e2e/auth.e2e.ts`) |
| The dock's push contract (`DOCK_PUSH_VAR`) | The rail copies it; changing it breaks both edges at once |
| The CV sheet's visual design | Client-facing artifact — §7 |
| Agent Surface IA | Settled by P1T-152; a re-skin is not the place to reopen it |

## 10. Slices

Sequential PRs off `main`, each merged before the next branches — no stacked bases. Slices 1 and 2
change no DOM structure at all, so the test suites stay untouched until slice 3.

1. ~~**P1T-159**~~ — tokens, both themes, Inter, `CssBaseline` floors — repaint only; the four colour sites are fixed here. **Shipped**, with zero edits to existing tests: 189 unit tests and the 11 Playwright specs stayed green untouched, which was the whole claim of a repaint
2. **P1T-160** — component overrides and the density pass
3. **P1T-161** — the rail, its CSS var, the mobile drawer, the persisted theme toggle
4. **P1T-162** — `PageHeader` and its adoption across the five pages, incl. per-page width
5. **P1T-163** — dock chrome refresh
6. **P1T-164** — `CvPage` light-lock
7. **P1T-165** (later) — ⌘K palette — it hangs off the shell and needs a search-endpoint story of its own

Each slice ends with Playwright screenshots in light and dark of: roster, employee detail, catalog,
users, sign-in, dock open, CV page. Screenshots land in `docs/` (gitignored); this record and
`manuals/spa-architecture.md` are what gets committed.

The capture is `web/e2e/screenshots.e2e.ts`, run with `npm run shots` — committed rather than
rebuilt from this paragraph six times, on the same reasoning as the repo's gate harnesses
(`CloudflareWorkersAiGateTests`, `CompatEndpointProbeTests`). It is skipped in the default e2e run,
because a screenshot is an artifact and not an assertion, but it stays inside the default run's
*compile* so it cannot rot between slices. Dark mode is driven by Playwright's `colorScheme`, which
sets `prefers-color-scheme` — so the capture exercises the real default path in `mode.ts` rather
than a pinned override.

## 11. Live risk between slice 1 and slice 6

Slice 1 makes dark mode **reachable by default** — an operator whose OS is dark gets it on the next
reload, with no toggle needed. §7's light-lock is slice 6. Until P1T-164 lands, a dark-OS operator
printing a CV gets a bad artifact, and worse than the obvious way: browsers drop background colours
from print unless the user enables background graphics, but they keep `color`, so the sheet prints
near-white text on white paper rather than white-on-dark. Confirmed in the slice-1 screenshots —
`docs/design-system-shots/dark/4-cv-page.png` is a dark sheet.

This is a property of the agreed order, not a defect in slice 1, and it is recorded here rather
than silently absorbed: **P1T-164 is the one child of this chain that is now urgent rather than
merely next.**
