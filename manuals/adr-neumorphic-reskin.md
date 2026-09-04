# ADR: the look reverses — relief replaces the hairline

**Status:** accepted, 2026-09-03. Supersedes §3 of `manuals/spa-design-system.md`, which P1T-198
rewrote on 2026-09-04 with the numbers as built rather than as guessed.

**This record landed after the code it describes, and that is a finding, not a footnote.** It was
written first, on 2026-09-03, precisely so the decision would precede the build — and then it sat
uncommitted on a working tree while P1T-195 … P1T-198 were implemented, reviewed and merged. For
that window `main` carried a fully neumorphic app, a §3 describing it, and a `CONTEXT.md` glossary
still insisting that *a shadow separates nothing on a near-black page* — plus a §3 citing an ADR
that did not exist. Written is not landed. The tracker said slice ⓪ was done because the files
existed on a disk, which is the same class of mistake as a test that asserts a token instead of a
colour: a check that passes without touching the thing it claims to hold.

## The decision

The SPA's visual language becomes **neumorphic relief**: depth is carried by a dual shadow — a dark
offset and a light one — so a surface reads as extruded from its ground or pressed into it. The
accent becomes amber `#F59E0B`. Type becomes Plus Jakarta Sans (headings), DM Sans (body) and
JetBrains Mono promoted from a code font to a UI role. This is taken from
`ExpertToJob Admin Prototype.html`, a six-artboard Claude Design canvas.

It is a **reversal**, not an evolution. `spa-design-system.md` §3 has said since P1T-159 that
separation is "a hairline border; shadows are the exception, not the mechanism", and `CONTEXT.md`'s
**Surface Ramp** entry said, in as many words, that *a shadow separates nothing on a near-black
page*. The prototype makes shadow the only mechanism, on `#151A28` — the exact near-black that
sentence was written about. Both statements are deleted rather than softened. A design record that
hedges its way from one position to the opposite one is worth less than a record that says it
changed its mind.

The library does not change: MUI 5.16 keeps every pixel, and the work is almost entirely
`web/src/theme/`. §1 of the design-system record priced Tailwind/shadcn and MUI 7 and rejected both;
none of the reasons moved. What changes is what the tokens say, not who reads them.

## Why now, and why wholesale

Half-adopting neumorphism was considered and rejected: taking the amber and the type while keeping
hairline-not-shadow is a recolour that keeps none of what makes the prototype look the way it does.
The extruded/inset language only reads as physical if everything obeys it. So either the record
reverses or the prototype is declined — there is no coherent middle.

## Consequences, priced before starting

- **Depth gets a hard ceiling.** Relief does not nest: inset-inside-extruded-inside-inset has no
  physical reading, and dual shadows at three levels turn to mud. **Two levels maximum**; anything
  deeper is a flat fill and a hairline. This is the rule that replaces the three-step ramp, and it
  is checkable by a component in a way "feel for the ramp" never was. Some of the eleven existing
  `well` Papers sit deeper than level two and become flat.
- **`warning` has to move.** Today's dark `warning.main` is `#F5B942`, half a step from the new
  accent. Two ambers meaning different things is worse than a warning that sits nearer to error, so
  `warning` re-hues and the accent stays exactly as drawn.
- **`info` becomes teal `#38B2AC`**, the prototype's one secondary hue. Blue beside amber is the
  thing that would read as a re-skin that stopped halfway.
- **Light mode loses its white surface.** Neumorphic light shadows are grey-plus-white, which only
  reads on a grey ground; on `#FFFFFF` the white half is invisible and the look collapses to an
  ordinary drop shadow. So light goes `#E5EAF3`/`#EEF1F8`, and the CV sheet — which stays white —
  now reads as a distinctly whiter card floating on grey.
- **The accessibility floors survive, and cost something.** Neumorphism's known failure is 1.4.11:
  a control whose edge is a shadow has no contrast-measurable boundary, and the prototype's own
  inputs are `border: 1px solid transparent`. The amber hairline stays decorative at `.25`, and
  `surface.outline` at 3:1 stays underneath it as the boundary that is actually measured — the split
  `tokens.ts` already makes between a decorative `divider` and a load-bearing outline. The
  prototype's light `#7A869A` secondary text fails 4.5:1 against its own `#EEF1F8` and is retuned;
  the artboards are the reference for the look, not for the numbers.
- **Density does not follow the drawing.** Slice 2 of P1T-158 made `size="small"` the default and
  deleted 109 explicit props to get there. The prototype's mass (12/18 padding, radius 20, 8px
  offsets) would undo that. Density is kept and the shadow offsets scale down to suit — the
  prototype's *ratios*, at our size.
- **The CV sheet had to be pinned, not merely left alone.** §7 froze it, but `CvPage` wraps it in
  the live `lightTheme`, so the freeze was true by accident: the new light ground would have turned
  the client-facing sheet grey and the new accent would have turned its section headings amber —
  and `CvSheet.lightLock.test.tsx` would have stayed green throughout, because it asserts against
  the tokens rather than against colours. A literal `cvSheetTheme` and literal assertions make the
  freeze real, and incidentally stop the SPA sheet drifting from the QuestPDF renderer, which has
  its own colours and never loads the SPA.
- **Shadows print.** A global `box-shadow: none` at print media joins the other floors in
  `baseline.ts`. The CV path proves people print from this app, and grey smudges on every card is
  what the alternative looks like.
- **The artboards show features this app does not have** — four stat cards, an All/Available/Paused
  segmented control, a roster search row. `ExpertsPage.tsx` is 125 lines: a header, a Paper, a
  table. Those are not built here. Building the components without the features would ship dead
  code, and building the features inside a styling chain hides real questions (what *is* "available
  today" as a filter? what does search query?) behind a paint job. They arrive with the feature that
  needs them.
- **Nothing structural moves**, so the three nets hold as they are: the 39 frozen `data-testid`
  hooks, the frozen accessible names and `BRAND`, the rail/dock push contracts, and the four
  MUI-class-coupled e2e selectors. Theme-layer work touches none of them.

## What was rejected

- **Prototype palette and type only, keeping hairlines** — the recolour described above.
- **Shifting the accent off `#F59E0B` to protect `warning`** — sacrifices the prototype's signature
  to protect the role with the least traffic in the app.
- **Following the prototype into `medium` density** — a 40% taller app is not a better one.
- **Building the seg control, stat card and inset toggle now** — the app owns one `<Checkbox>` and
  zero `Switch`, `Radio`, `Tabs` or `ToggleButton`. There is nothing for them to be.
- **Letting the CV sheet follow the new palette** — defensible, but it drags the server-side PDF
  renderer into a styling slice, and the sheet is the one artifact a client sees.
- **Re-skinning only the six prototyped screens** — two visual languages is worse than either.
