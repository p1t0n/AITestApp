---
name: ExpertToJob
description: Dense, dark-first staffing workbench in neumorphic relief, lit by one amber accent.
colors:
  primary: "#F59E0B"
  primary-light: "#FBBF24"
  primary-deep: "#B4700A"
  primary-ink: "#8A5406"
  night-page: "#151A28"
  night-surface: "#1F2A3A"
  night-raised: "#28303E"
  night-outline: "#7C8899"
  night-divider: "#434D5E"
  night-text: "#E8ECF4"
  night-text-muted: "#9FAAB9"
  mist-page: "#E5EAF3"
  mist-surface: "#EEF1F8"
  mist-raised: "#DCE3ED"
  mist-outline: "#6E7684"
  mist-divider: "#B4BECD"
  mist-text: "#1B2331"
  mist-text-muted: "#535E70"
  error: "#FF6B6B"
  error-light-mode: "#C62828"
  warning: "#F97316"
  warning-light-mode: "#9A4A0C"
  info: "#38B2AC"
  info-light-mode: "#2A7374"
  success: "#4ECB86"
  success-light-mode: "#1B7A47"
typography:
  headline:
    fontFamily: "Plus Jakarta Sans Variable, DM Sans Variable, system-ui, sans-serif"
    fontSize: "2rem"
    fontWeight: 800
    lineHeight: 1.2
    letterSpacing: "-0.02em"
  title:
    fontFamily: "Plus Jakarta Sans Variable, DM Sans Variable, system-ui, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 800
    lineHeight: 1.35
  body:
    fontFamily: "DM Sans Variable, system-ui, -apple-system, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.55
  body-small:
    fontFamily: "DM Sans Variable, system-ui, -apple-system, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "DM Sans Variable, system-ui, -apple-system, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 600
  eyebrow:
    fontFamily: "JetBrains Mono Variable, ui-monospace, SFMono-Regular, Menlo, monospace"
    fontSize: "0.6875rem"
    fontWeight: 700
    lineHeight: 1.6
    letterSpacing: "0.08em"
rounded:
  small: "8px"
  medium: "14px"
  large: "24px"
  pill: "999px"
spacing:
  unit: "8px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.night-page}"
    typography: "{typography.label}"
    rounded: "{rounded.medium}"
  button-secondary:
    backgroundColor: "{colors.night-surface}"
    textColor: "{colors.night-text}"
    typography: "{typography.label}"
    rounded: "{rounded.medium}"
  input-well:
    backgroundColor: "{colors.night-page}"
    textColor: "{colors.night-text}"
    rounded: "{rounded.medium}"
  chip-tag:
    backgroundColor: "{colors.night-raised}"
    textColor: "{colors.night-text}"
    typography: "{typography.eyebrow}"
    rounded: "{rounded.pill}"
  table-head:
    backgroundColor: "{colors.night-raised}"
    textColor: "{colors.night-text-muted}"
    typography: "{typography.eyebrow}"
  panel:
    backgroundColor: "{colors.night-surface}"
    textColor: "{colors.night-text}"
    rounded: "{rounded.medium}"
---

# Design System: ExpertToJob

## Overview

**Creative North Star: "The Lit Workbench"**

A dense, dark-first product UI where every panel is extruded from one ground like a matte tool on a bench, and a single amber light marks the thing you can act on. It is built for two equal audiences, the Administrator choosing Experts for a Job and the Expert keeping their own CV honest, so it is a working surface first: tables, forms, a streaming agents dock. Body copy is 14px, headings stop well short of marketing scale, and the personality lives in precise detail rather than ornament.

Depth is relief. A dark shadow and a light shadow together make a surface read as lifted out of its ground or pressed into it. The look is governed by `manuals/adr-neumorphic-reskin.md` (accepted 2026-09-03) and is a binding brand commitment from PRODUCT.md. Light mode is an equal citizen, a re-tuned set of the same roles on a grey ground, never a washed-out dark.

Honesty is visible in the surface: boundaries that can be measured sit under the soft relief, evidence and `inferred` badges are tags not decoration, and degraded states look like states.

**Key Characteristics:**
- Dark first (blued near-black), light mode equal, no white surface in either.
- Relief Depth: two levels of shadow, then flat fill and hairline.
- Amber on the primary action only; everything else neutral chrome.
- Three type roles: Plus Jakarta Sans 800 for headings, DM Sans for body, JetBrains Mono for eyebrows, table heads and tags.
- 14px base, `size="small"` everywhere, tabular numerals.
- Motion ceiling 150ms, off under `prefers-reduced-motion`.
- Contrast and relief are asserted in tests, not eyeballed.

## Colors

A blued near-black ground with cool grey-blue text, one amber accent, and teal as the only secondary hue.

### Primary
- **Workbench Amber** (#F59E0B): the primary action fill and selected washes. A fill labelled in ink, never white text (8:1 ink on it).
- **Lamp Glow** (#FBBF24): the lighter step for hover and hairline draw.
- **Banked Amber** (#B4700A, dark mode) / **Deep Amber** (#8A5406, light mode): the step that is read as text or measured as a boundary. In light mode it doubles as the focus ring, because #F59E0B is only 1.9:1 on the grey ground.

### Secondary
- **Bench Teal** (#38B2AC dark, #2A7374 light): the single secondary hue, used for `info`. Blue beside amber reads as an unfinished re-skin.

### Neutral (dark)
- **Night Ground** (#151A28): the page. Never pure black, because black shadows on black cast nothing.
- **Night Panel** (#1F2A3A): panels and cards (`background.paper`).
- **Night Raised** (#28303E): a flat fill inside a panel, e.g. a table head or code span. Level three, no shadow.
- **Control Edge** (#7C8899): a control's boundary, held to 3:1 on all surfaces.
- **Hairline** (#434D5E): decorative divider, deliberately below 3:1 (floor 1.4:1).
- **Ink** (#E8ECF4) and **Haze** (#9FAAB9): primary and secondary text.

### Neutral (light)
- **Mist Ground** (#E5EAF3), **Mist Panel** (#EEF1F8), **Mist Raised** (#DCE3ED), **Mist Edge** (#6E7684), **Mist Hairline** (#B4BECD), **Slate Ink** (#1B2331), **Slate Haze** (#535E70).

### Semantic
- Error #FF6B6B / #C62828, warning #F97316 / #9A4A0C (re-hued off the accent so two ambers never mean two things), success #4ECB86 / #1B7A47, info as above. Dark / light mode values.

### Named Rules
**The One Light Rule.** Amber marks the primary action and selection only. Outlined and text primary buttons render as neutral chrome; an accent that is everywhere means nothing.
**The Measured Edge Rule.** A shadow edge has nothing to measure (WCAG 1.4.11). Every control keeps a real `outline` line under its relief.
**The Readable Step Rule.** In light mode, role colour used as text or boundary takes the `dark` step, never `main`.

## Typography

**Display/Heading Font:** Plus Jakarta Sans Variable, weight 800 (fallback DM Sans, system-ui)
**Body Font:** DM Sans Variable (fallback system-ui)
**Label/Mono Font:** JetBrains Mono Variable (fallback ui-monospace stack)

**Character:** A confident geometric heading voice over a quiet, legible body, with mono promoted to a UI role so counts and column names read as numbers and line up. Fonts are self-hosted via Fontsource; no CDN.

### Hierarchy
- **Headline** (800, 2rem, 1.2, -0.02em): page titles (h1); h2 1.625rem, h3 1.375rem, h4 1.25rem step down.
- **Title** (800, 1.0625rem / 0.9375rem, 1.35–1.4): panel and section headings (h5, h6).
- **Body** (400, 0.875rem, 1.55): default copy; `body-small` 0.8125rem for table and dense text. Keep prose near 65–75ch.
- **Label** (600, 0.8125rem, no transform): buttons and subtitles. Never shouted.
- **Eyebrow** (mono 700, 0.6875rem, 0.08em, uppercase, tabular-nums): `ROSTER · 42 RECORDS`, table heads, tags.

### Named Rules
**The Mono Is A Role Rule.** Mono is for eyebrows, table heads, tags and numerals, never body prose.

## Layout

A left rail plus a content area, with the agents assistant as a dock that pushes the content (rail and dock push contracts are frozen and e2e-asserted). Density is compact: spacing unit 8px, `size="small"` on all interactive MUI components, table cell padding 6px 12px, 14px base size scaling the whole rem scale. Pages are tables, forms and streams, not marketing sections. Responsive and print behaviour are settled in a real browser; the rail and dock hide at print.

## Elevation & Depth

Depth is relief, structural rather than decorative. A surface is either extruded (lifted) or inset (pressed), and Relief Depth stops there: anything deeper is a flat `raised` fill plus a hairline, enforced by a descendant selector in `components.ts`, not by memory. Float is the largest pair plus backdrop blur and means "above the page" (menus, dialogs, autocomplete, the undocked agent panel). Relief does not print.

### Shadow Vocabulary
- **Extruded** (dark: `6px 6px 9px rgba(0,0,0,.5), -6px -6px 9px rgba(255,255,255,.12)`): panels, cards, resting buttons.
- **Extruded small** (dark: `3px 3px 6px rgba(0,0,0,.45), -3px -3px 6px rgba(255,255,255,.10)`): buttons, chips, icon buttons.
- **Inset** (dark: `inset 4px 4px 6px rgba(0,0,0,.5), inset -4px -4px 6px rgba(255,255,255,.12)`): Wells such as message bubbles and read-only blocks.
- **Inset small** (dark: `inset 2px 2px 4px rgba(0,0,0,.5), inset -2px -2px 4px rgba(255,255,255,.10)`): text fields and the pressed button.
- **Float** (dark: `9px 9px 21px rgba(0,0,0,.6), -8px -8px 18px rgba(255,255,255,.10)` + `blur(8px)`): overlays.
- Light mode swaps to slate-grey `rgba(163,177,198,…)` darks and near-white lights; there is no white surface because white-on-white relief is invisible.

### Named Rules
**The Two Levels Rule.** One thing lifted, one thing pressed into it, nothing deeper. A third level is a flat fill and a hairline.
**The Printed Flat Rule.** At print media every `box-shadow` is `none !important`.

## Shapes

Soft, generous corners in three steps: small 8px (inside a control, focus ring, menu item), medium 14px (Paper, Button, Menu, Alert), large 24px (brand tile, agent panel). Tags are pills (999px), the one exception. Control boundaries are 1px lines; focus is a 2px solid ring offset 2px, applied globally to `:focus-visible`.

## Components

Tactile, calm, precise: soft-pressed controls, accent only on the primary action, mono tags, measurable boundaries under the relief.

### Buttons
- **Shape:** 14px radius, small size, no elevation, label 600 not uppercased.
- **Primary:** amber fill, ink label, extruded small.
- **Press:** active swaps to inset small and `translateY(2px)` within the 150ms ceiling.
- **Secondary / Ghost:** outlined = neutral text with `outline` border, hover steps to Haze; text = no shadow, hover wash. `color="error"` stays destructive.

### Chips
- **Style:** mono, uppercase, 0.04em tracking, pill; neutral raised fill by default.
- **State:** a role colour gives a 10% wash with the readable step as ink and a 40% edge; ink-on-wash is asserted as a composite.

### Cards / Containers
- **Corner Style:** 14px. **Background:** panel. **Shadow:** extruded. **Border:** none (a Paper inside two Papers goes flat with a hairline). Wells are inset on the page ground.

### Inputs / Fields
- **Style:** a Well (inset small on page ground) with a 1px `outline` boundary kept underneath.
- **Focus:** border becomes 2px focus-ring colour. Hover steps to the stronger neutral first, never louder than focus. Labels float wholly above the field.

### Tables
- Compact; head is raised fill with mono eyebrow type; rules in hairline; hovered row takes an 8% amber wash.

### Navigation
- Left rail is part of the ground: hairline edge, no relief. Selected items take the 16% amber wash. List items use the 8px radius.

### Alerts
- Tinted panel (14% role wash, 40% edge), role-coloured icon, message left in primary text for contrast.

### Overlays
- Dialog, Menu, Autocomplete and Tooltip use Float; tooltip is the raised surface.

## Do's and Don'ts

### Do:
- **Do** express tokens through MUI palette roles (`background.paper`, `divider`, `text.secondary`); never a token name in a component.
- **Do** put a look needed twice in `components.ts`; `sx` is for spacing and layout.
- **Do** keep a measurable boundary on every control and assert contrast on tokens and rendered composites.
- **Do** use the pinned vocabulary (Expert, Roster, Bench, Job, Administrator) in all labels.
- **Do** show degraded and `inferred` states as plain tags or notes, with evidence visible.

### Don't:
- **Don't** add a third relief level or inset-inside-inset.
- **Don't** put amber on secondary actions or use #F59E0B as text or a boundary in light mode.
- **Don't** introduce a white surface in either mode.
- **Don't** rename or silently add `data-testid`s or the frozen accessible names (`web/src/frozenHooks.test.ts`, spa-design-system §9).
- **Don't** use React Context for theme or session; use `useSyncExternalStore` modules.
- **Don't** set a bare `borderColor` on a Paper: `MuiPaper` resets the border to 0, so it draws nothing. Pair it with `border: 1`.
- **Don't** expect extra relief inside the agents dock: the dock is already a Float Paper, so Papers within it are flat by the Two Levels Rule. Weight there comes from position, the accent edge and the button.
- **Don't** set mono in body prose or shout labels with uppercase buttons.
- **Don't** fabricate customers, testimonials or Expert data in mocks.
