# SPA architecture

> **Status (2026-08-30):** description of what is there, not a proposal. Covers all of `web/` —
> the roster screens, the agent dock, and the passkey auth journey. Domain vocabulary lives in
> `CONTEXT.md`; this record is about structure.

React 18 + TypeScript on Vite, MUI 5 for every pixel, TanStack Query v5 for every server read,
React Router v6 for navigation. ~7,100 lines across 38 source files. No state-management library,
no component library beyond MUI, no CSS framework — there are 13 lines of hand-written CSS in the
whole app and they exist only for printing.

## 1. Two backends, one token

The SPA talks to two services, proxied by Vite in development (`web/vite.config.ts`):

| path | service | dev port |
|---|---|---|
| `/api/*` | Web host — all CRUD, auth, CV, PDF | 5069 |
| `/agents/*` | Agents host — every AI surface | 5200 |

They are separate axios instances, `http` and `agentHttp` (`src/api/http.ts`), and both get the same
request interceptor attaching the same bearer token. That works because the Web host issues the
session JWT and both hosts validate it with a shared signing key — so the SPA never learns that
two services exist beyond the base URL.

The e2e harness overrides both targets by env var (`VITE_API_TARGET`, `VITE_AGENTS_TARGET`) so a
suite run never collides with a dev stack on the default ports.

## 2. The shell

`main.tsx` mounts four providers and nothing else: `QueryClientProvider` (with
`refetchOnWindowFocus: false`), MUI `ThemeProvider` + `CssBaseline`, and `BrowserRouter`.

The theme is a token layer in `src/theme/` (P1T-159): `tokens.ts` declares the values once,
`index.ts` builds `lightTheme` and `darkTheme` from them, `baseline.ts` holds the accessibility
floors, `components.ts` holds the component overrides (P1T-160) and `mode.ts` decides which theme is
in force. The split between the last two is the one worth knowing: `baseline.ts` is what has no
component to hang off (the focus ring, the scrollbar, `::selection`), `components.ts` is everything
that does — and per the **Override Policy**, a look needed twice belongs there rather than in a third
`sx`. The `sx` blocks that remain are spacing and layout. Components read **MUI palette keys**
(`background.default`, `surface.raised`, `divider`, `text.secondary`) and never import a token —
that is the point of the layer, not an oversight: one vocabulary, so dark mode costs a component
nothing. Reasoning and the full look rules are in `manuals/spa-design-system.md`.

The only palette role this app adds is `surface`, module-augmented with `raised` (the third surface
step, which MUI has no name for) and `outline` (a control's own boundary, which has to clear 3:1
where `divider` deliberately does not). `background.default` / `background.paper` are *not*
re-exposed under `surface` — a second name for the same colour is what this layer exists to avoid.

> **Still scheduled to change (P1T-158).** The dock chrome refresh (slice 5) is not built. This
> section describes the shell as it stands after slice 4. The CV-sheet light-lock (§9) was slice 6
> and **is** built — it was pulled ahead of the rest because slice 1 made dark mode reachable by
> default and left a client-facing document exposed; `manuals/spa-design-system.md` §11 records why
> and what it cost.

`App.tsx` is the whole chrome: the rail (or, signed out, a slim public top bar), the route table,
and the agent dock. It holds no data — and since P1T-162 it holds no page container either. Width
is a per-page decision, stated once at the top of each page through `PageHeader` (below).

### One heading strip, and width decided per page

`components/PageHeader.tsx` (P1T-162) is the only title strip in the app: the page's `<h1>`, an
optional back link, a right slot for its primary actions, sticky with a border that appears once it
is pinned. All five routed pages render through it and none renders a title of its own.

**The page body comes through the header**, which is the part worth explaining. Width is a per-page
prop (`wide` → 1440px for the three tables, `content` → 1000px for the profile and the CV page), and
a header capped at one width above a body capped at another is a defect nobody would notice for
months. Passing the body in makes the two incapable of disagreeing. `PageContainer` is exported
without the strip for the one caller that needs the box and no heading: the routed-area error
fallback, which renders *instead of* a page.

The strip pins **under** whatever the rail is covering at the top, read from a third published
property — `--app-rail-top-inset`, `0px` while the rail stands beside the app and the height of its
bar while the rail is a mobile drawer. Same contract as the two side pushes, for the same reason:
the header would otherwise have to know the rail's breakpoint and the height of a dense `Toolbar`.

"Pinned" is measured as the gap between the strip and a zero-height sentinel left at its place in
the flow, not as `scrollY > 0` — so the border appears when the strip actually stops moving,
whatever the inset currently is, and the border is always 1px (transparent until then) so gaining
it moves nothing.

The whole strip is print-hidden, and the strip is what carries the rule — every control inside it
goes with it, because `display` does not inherit but layout does. That is the mechanism the print
specs measure, and `PageContainer` deliberately drops its cap and gutters at print media so a CV is
the page rather than an inset panel.

### Two edges that push

The shell has two `position: fixed` edges — the rail on the left (`components/AppRail.tsx` +
`useAppRail.ts`, P1T-161) and the agent dock on the right (`useAgentDock.ts`, P1T-154) — and it
treats them identically:

**Each edge publishes how much of the viewport it is covering as a CSS custom property, and the root
`Box` pads by both.** `--app-rail-push` and `--agent-dock-push`, each read as
`var(…, 0px)`. Neither edge participates in layout, and the shell knows neither one's state, width,
or breakpoint. The property is written by the *component*, not the hook, so it exists exactly as
long as there is an edge to make room for: the auth pages render no rail and signing out unmounts
the dock, and in both cases the `0px` fallback closes the gap with no condition anywhere.

Both gutters are also zeroed under `@media print`, because neither edge prints and an un-zeroed
240px gutter would shift every printed artifact — including a client's CV — to the right.

**The one thing that does know about both edges is the squeeze rule**, and it lives in `App` because
`App` is the only place with both hooks. Both edges push, so on a narrow-ish viewport the middle
column is what pays. `railSqueezeQuery(dockPush)` turns the numbers into one media query — the rail
gives up its labels (240 → 64px) the moment an expanded rail would take the routed content below
`RAIL_CONTENT_FLOOR` (720px). At 1280px with a 420px dock the rail is forced collapsed and the
content gets 796px; at 1440px both fit and it stays expanded. That is why the content is legible at
either width: a rule, not luck. `dockPushWidth(dock)` is exported from the dock's own module so the
expression for "what the dock covers" still exists in exactly one place.

State is persisted where a person would expect it to be: the rail's collapse and the dock's
mode/width survive a reload; the mobile drawer's open state and the dock's `open` do not.

## 3. Auth and session — global state without a Context

There is no React Context anywhere in this app. Two pieces of state cross-cut it, and both use the
same shape: the session token (`src/auth/session.ts`, below) and the Theme Mode
(`src/theme/mode.ts`, P1T-159). The second was written by copying the first on purpose — one
pattern to learn, and the pattern is `localStorage` + a `Set` of listeners + `useSyncExternalStore`.

The Theme Mode store differs in exactly one way, and it is a difference in the *question*, not the
mechanism: storage holds an **override**, not the answer. `getMode()` is the override if there is
one, else `prefers-color-scheme`, so `subscribe` has a third source to listen to — the OS media
query — and the absence of a stored value is a meaningful state rather than a missing one.

The session token:

- `localStorage` is the source of truth, because the axios interceptor reads it synchronously and
  is not a React consumer.
- A `Set` of listeners is notified on every write, and a `storage` event listener picks up
  sign-in/out **from another tab**.
- `useIsAuthenticated()` (`src/auth/useAuth.ts`) wraps that in `useSyncExternalStore`.

It is deliberately **presence-only**: it checks that a token exists, never that it is valid. The
server is the authority and answers 401. That keeps the client free of expiry logic and of a token
parser.

`RequireAuth` in `App.tsx` is a route-element guard — renders `<Outlet/>` when authed, otherwise
`<Navigate to="/signin" replace/>`.

The passkey ceremonies themselves are in `src/auth/webauthn.ts`, driven from three mutation hooks
in `src/api/auth.ts` (`useSignup`, `useSignin`, `useRecover`). Each is a two-step server round trip
(`/begin` → authenticator → `/complete`) and calls `setSession` on success, which is what makes the
whole app re-render into its signed-in state.

## 4. Routing

Flat, eager, and split by audience — every protected route declares whose it is, mirroring the
server, where the audience is declared per endpoint and the fallback is staff-only:

```
/signin  /signup  /recover          public

                                    ServiceManager
/                                     ExpertsPage
/experts/:id                          ExpertDetailPage
/experts/:id/cv                       CvPage
/catalog                              CatalogPage
/users                                UsersPage

                                    Expert
/me                                   → /me/cv
/me/cv                                MyCvPage
/me/claim                             ClaimStatusPage
/me/privacy                           MyWorkspacePage
```

A signed-in person who asks for a route their role cannot have goes to **their own landing page,
never `/signin`** — sending them to the gate would claim they are signed out, and offer them a
second account they do not have. `landingFor` is the one place that decides where that is.

Every page is a static import — no `React.lazy`, no `Suspense`, so the bundle is one chunk. At 38
files that is a defensible trade; it is worth revisiting only if a heavy dependency lands.

### The Expert's half (P1T-190)

**Two places, and not more**: *My CV* and *Privacy & data*. An Expert has no other account settings
worth a page — their email is immutable to them (P1T-184) and they do not set their own token caps.

**My CV is the landing**, because editing it is what they came to do; a dashboard reporting three
mostly-static facts is a page that tells you nothing on most visits, so profile status is a compact
strip on the editor instead. Somebody who owns no record goes to `/me/claim` — there is nothing to
edit, and an empty editor would read as "fill this in" rather than "you are waiting on somebody".

**The child forms are one shared module** (`components/ExpertRecordSections`) used by the staff page
and the Expert's alike; the *pages* are thin shells around it. What was rejected is the other shape,
one page serving both roles with controls hidden by role — "hidden for Experts" is one `sx` prop away
from not hidden, and that is how a page grows holes. A test reads both page sources and fails if
either reaches past the shared module to a child form directly.

**Same shell, dock not mounted.** The agent surfaces read and act on the whole roster, so they are
staff's. This is not a second layout mode: the dock is already closeable, so "rail only" is a state
the Content Floor handles today. ⌘K rides the same condition — two of the three things it offers are
the roster and the agent surfaces, and an Expert can reach neither.

## 5. The data layer: `src/api/`

**Eighteen modules behind one barrel, ~115 exported names, largest module 147 lines.** It is the
SPA's only architectural layer. It used to be a single 1,130-line `api.ts` holding five different
kinds of thing; P1T-151 split it by domain without changing a single component import:

```
src/api/
  index.ts           the barrel — every component imports from here, and only from here
  http.ts            the two axios clients, the token interceptor, apiErrorMessage
  auth.ts            the three passkey ceremonies (§3) + the local session helpers
  experts.ts       the roster aggregate: list, detail, CV, cv.pdf, promote
  expertChildren.ts  skills, availability, languages, qualifications, experiences
  catalog.ts         the skill-catalog tree
  users.ts           user administration and cap overrides
  agents/
    shared.ts        AgentJobRequest / AgentAnswer, and the JD-extraction contract
    usage.ts  rosterQa.ts  tailoring.ts  match.ts  bench.ts  interviewKit.ts
    shortlist.ts  staffing.ts  proposals.ts  rosterScan.ts  ingestion.ts
```

**The one-import-site rule is the point of the barrel.** Components import from exactly one place
and nothing else in the app knows a URL, a query key, or which of the two backends serves a call.
That property is what made the file large in the first place, so the split preserves it rather than
trading it away: the barrel is the public face, the modules behind it are the readable unit.

Two things are not hooks at all and are called imperatively: `runStaffing` (SSE, in
`agents/staffing.ts` — see below) and the blob → object URL → synthetic `<a download>` click →
revoke dance inside `useDownloadCvPdf` (`experts.ts`).

Splitting a data layer with a shared cache has one rule worth stating: **a module boundary must not
become a cache boundary.** The query-key convention below is a single flat namespace across all
eighteen modules, so `agents/ingestion.ts` invalidating `["experts"]` and `experts.ts` reading
it are the same key by construction. The split is by *who owns the endpoint*, never by cache
region.

### Query keys

A flat, hierarchical convention, invalidated by prefix:

```
["experts"]                    list
["experts", id]                detail
["experts", id, "cv"]          assembled CV
["categories"] ["categories","tree"] ["skills"]
["users"] ["usage"]
["staffing-proposals", status]
["roster-scan", jobId]
```

Invalidation is explicit and generous. Every child mutation (skills, availability, languages,
qualifications, experiences) invalidates `["experts", expertId]`; the three experience
mutations also invalidate the CV key, because an experience edit changes the rendered CV.
`["experts", id, "cv"]` is never invalidated by anything else.

**Every agent mutation invalidates `["usage"]`.** That is what keeps the dock's Usage tab honest
without polling — the token ledger updates the moment any agent call returns.

### Three transports, on purpose

| shape | used by | why |
|---|---|---|
| `useQuery` / `useMutation` | all CRUD, catalog, users, usage, single-shot agents | default |
| `useQuery` + `refetchInterval` | `useRosterScanJob` | long async job; polls at 3s and **returns `false` once the job is terminal**, so a finished job stops costing requests |
| `fetch` + `ReadableStream` | `runStaffing` | axios buffers whole responses, so a progress stream cannot go through it |

`src/sse.ts` is a hand-written SSE-over-POST client — ~120 lines, scoped to exactly what the
server emits (`event:`/`data:`, `:` keep-alives, blank-line frames, LF or CRLF). It exists because
the staffing pipeline streams `step` frames over a **POST**, and `EventSource` only does GET.

It also carries `SseHttpError`, which extracts its message the same way `apiErrorMessage` reads an
axios error (`error ?? detail ?? title`) — so a pre-stream 429 cap response surfaces identically
to any other API failure, and the parsed body rides along for the structured cap payload.

## 6. Types

Split by origin, not by shape:

- `src/types.ts` (190 lines) — the roster domain: `ExpertDetail`, `Experience`, `SkillLevel`,
  the `Save*` write shapes, `Cv`.
- `src/api/agents/*.ts` — every agent contract type, declared beside the hook that returns it.

The split is by origin: the roster domain is the *server's* model, shared by every screen and
outliving any one call, so it has a module of its own. An agent contract is the shape of one
endpoint's reply and means nothing away from the hook that returns it, so it lives there.

P1T-151 kept that distinction and fixed the placement it was previously confused with: agent
contracts used to sit inline in the monolithic data layer, which read as "types go in `types.ts`,
except when they don't". Now each contract sits in its own agent module — `ShortlistResponse` in
`agents/shortlist.ts`, `HandoffPackage` in `agents/proposals.ts` — and the only types shared by two
agent surfaces are in `agents/shared.ts`. Both still re-export through the barrel, so a component
importing `type StaffingReport` from `"../api"` cannot tell the difference.

## 7. The agent dock

`AgentWidget.tsx` (210 lines) is a single floating panel holding **ten** tabs:

```
Roster Q&A · Tailor CV · Match · Interview · Shortlist
Staffing · Scan · Bench · Ingest · Usage
```

### Three layout modes

Driven by `useAgentDock` plus a `useMediaQuery("(max-width:600px)")` in `App`:

| mode | geometry |
|---|---|
| floating | 460×620 card, bottom-right, rounded |
| docked wide | full-height right sidebar, drag-resizable from its left edge |
| docked narrow | full-screen overlay, no resize |

`open` is session-only (always starts closed); `docked` and `width` persist to `localStorage`, so
the dock reopens the way it was left. Width is clamped to `[360, viewport/2]` inside the hook, and
the resize handler is plain `mousemove`/`mouseup` on `window` with `userSelect` suppressed for the
drag.

### Remount-as-reset

The rendering branch is a ternary chain keyed by mode, and **every panel gets a `key`**. That is
load-bearing, not incidental: switching tabs unmounts the previous panel, so each tab keeps
independent state and a half-finished shortlist never bleeds into a staffing run. The same trick
appears twice more:

- `AgentJobForm` is keyed by `${mode}-${expertId}` when prefilled, so a new drill-in always
  re-seeds its fields.
- On the roster screens, each child form dialog is **rendered only while its row is being edited**
  (`{languageEdit && <LanguageFormDialog .../>}`), because the dialogs seed from `initial` on first
  render only. A dialog kept mounted across two rows would show the first row's values for the
  second.

Mounting *is* the initialisation, everywhere in this app.

### Cross-tab drill-in

`AgentWidget` holds a `prefill` slot. A shortlist card's "Run full Match", or a staffing card's
"Open in Match" / "Tailor CV", calls up to `openPrefilled(target, expertId, jd)`, which switches
the tab and seeds the form. Any manual tab click clears it, so a stale prefill cannot resurface.

This is the only cross-tab communication in the dock; the tabs are otherwise fully isolated.

### One generic form for three agents

`AgentJobTab.tsx` (`AgentJobForm`) serves `cv-tailoring`, `match` and `interview-kit` from one
component, branching on `mode` for its labels, its hook, and its result renderer. The other seven
tabs each have their own panel, because their inputs and outputs have nothing in common.

### Human write authority, in the UI

`IngestionTab` is where the agent's write boundary becomes visible. The agent stages a **draft**
expert; the panel then renders the draft for review — skill proposals, degradation notes,
duplicate warning — and a human presses Promote. Nothing reaches the roster until then. Similarly
`RewriteCard` in `AgentJobTab` applies a tailoring rewrite **through the Web API with the user's
session**, never through the agent.

## 8. Roster screens

`ExpertDetailPage.tsx` (442 lines) is the largest screen and pulls sixteen hooks. It is composed
from a local `Section` helper (Paper + title + optional action + divider) repeated per child
collection, and four dialog components.

The write shapes echo the API's own semantics rather than smoothing them over: `toSaveExperience`
flattens achievements and skill links **into** the experience payload, because an experience save
replaces both lists wholesale. The form is a nested-collection editor, not three resources.

`ExpertsPage` and `CatalogPage` are list + dialog. `UsersPage` is the admin surface.

## 9. CV and printing

`CvPage` renders the assembled CV into a `Paper` with `id="cv-sheet"`, and offers two exits:

- **Print** — `window.print()`. The print styling is **colocated**, not global (P1T-154): the sheet
  flattens itself (`boxShadow: "none"`, `margin: 0`) and every piece of chrome hides itself, all in
  its own `sx` — the page toolbar, the rail and its mobile bar (`AppRail.tsx`), the signed-out top bar
  (`App.tsx`), and the agent dock in both of its shapes (`AgentWidget.tsx`, added by P1T-166). The
  root element additionally zeroes the two gutters the rail and the dock publish, since a `fixed`
  edge that is no longer painted still leaves its padding behind. `index.css` is down to one rule —
  `body`'s print background, which no component owns. The `#cv-sheet` id survives only as the e2e
  suite's locator.
- **Download PDF** — `useDownloadCvPdf` fetches a blob from the server-side QuestPDF renderer and
  saves it, parsing the filename out of `content-disposition`.

The two exist side by side deliberately: the server render is the canonical document, the browser
print is the human-driven fallback. They are also independent — `CvPdfRenderer` is a pure function of
`CvDto` with QuestPDF's own colours and never loads the SPA, so nothing in this section can change
what a `cv.pdf` download contains.

**The sheet is light-locked (P1T-164).** The `Paper` subtree renders under a nested `lightTheme`
`ThemeProvider` whatever mode the app is in, because what a client receives cannot depend on which
Theme Mode the operator happened to be using. One provider at one boundary, rather than a `@media
print` colour block that would have to stay exhaustive as the sheet grows sections — and it makes the
screen match the paper, so there is no surprise at the print dialog. `Paper` is the mechanism, not the
provider: MUI's root sets `color: text.primary` as well as `background.paper`, which is what re-colours
the eight `<Typography>`s in the sheet that name no palette key. Held by
`CvSheet.lightLock.test.tsx` (resolved colour in a dark app) and `e2e/cv-print.e2e.ts` (the same claim
in a real Chromium at print media, where jsdom cannot go).

**Print is verified at two layers on purpose, and only one of them is load-bearing (P1T-166).**
`src/test/printCss.ts` reads the CSS a component *emits* and checks the `@media print` block carries
the element's own generated class — cheap, and it runs in the default `npm test`, so deleting a print
rule is red without a database or a browser. What it cannot answer is whether the declaration *wins*:
jsdom implements no cascade and never re-evaluates a media query, so an emitted rule that loses a
specificity tie reads exactly like one that takes. `e2e/print.e2e.ts` and `e2e/cv-print.e2e.ts` answer
that under `page.emulateMedia({ media: "print" })`, where Chromium recomputes the cascade for real,
and each of them also reads back at screen media — a rule that leaked out of `@media print` would
otherwise satisfy every print assertion while breaking the page it was meant to leave alone. The
division of labour matters because the e2e layer is the one that finds things: it is what caught the
dock having no print rule at all.

## 10. Error handling

There is no error boundary and no global error surface. Every component that can fail holds its
own `useState<string | null>(null)` — eleven of them do — and renders the message inline.

The convergence point is `apiErrorMessage(err)`, which unwraps an axios error to
`error ?? detail ?? title ?? err.message`. `SseHttpError` mirrors that shape for the streaming
path, so both transports produce the same string for the same server response.

Cheap and predictable. It means an unexpected render-time throw takes the page down, and it means
the eleven error slots each re-implement placement and styling.

## 11. Testing layers

| layer | tool | what it covers |
|---|---|---|
| component | vitest + Testing Library (jsdom) | 12 specs, ten of them one-per-dock-tab (`AgentWidget.staffing.test.tsx`, …) plus `ChildFormDialogs` and `api.applyRewrite` |
| e2e | Playwright + CDP virtual authenticator | `auth`, `roster`, `expert-children` — the passkey-gated journeys |

The component specs are organised by dock tab, which mirrors the dock's own isolation: each tab is
independently mountable, so each is independently testable.

The e2e suite runs `fullyParallel: false, workers: 1` and shares one roster — specs keep apart by
owning the rows and accounts they create. Its stack is started by `web/e2e/run.mjs` (database and
API first, because the API cannot boot without a database), and Playwright starts only the SPA,
which has no such dependency.

## 12. Decisions, and what was passed over

**`localStorage` + `useSyncExternalStore` for session, not Context.** The axios interceptor needs
the token synchronously outside React, so `localStorage` had to be the source of truth regardless.
Given that, a Context would have been a second copy to keep in sync. The subscription also gets
cross-tab sign-out for free via the `storage` event, which a Context cannot do.

**Presence-only auth, no client-side token validation.** The server already answers 401 and is the
only party that can be trusted about expiry. Parsing the JWT client-side would add a second,
divergent opinion about whether the user is signed in.

**One import site rather than per-domain import paths.** Every component imports from one place
and no component knows a URL. That was originally one 1,130-line `api.ts`; P1T-151 kept the rule and
dropped the cost by putting a barrel in front of per-domain modules (§5). Components were left
alone deliberately — rewriting ~35 import statements would have made a pure move look like a
refactor and cost the reviewer the ability to read the diff as "nothing moved but the file
boundaries".

**Remount-as-reset rather than explicit reset effects.** Keying a panel by mode makes "this tab
forgets when you leave it" a structural fact instead of a `useEffect` that has to be kept correct
as state grows.

**A hand-written SSE client rather than a library.** The staffing stream is a POST, which rules
out `EventSource`; the frame grammar the server actually emits is small enough to parse in 120
lines, and doing so keeps `SseHttpError` aligned with `apiErrorMessage`.

**Polling that stops.** `useRosterScanJob` returns `false` from `refetchInterval` on a terminal
state rather than unmounting or clearing an interval by hand — the query settles itself.

**No state-management library.** Server state is TanStack Query's; the only client state that
outlives a component is the session and the dock layout, and both are a `localStorage` key.

## 13. Where it strains

Filed as P1T-151 (api.ts split, incl. type placement — **shipped**), P1T-152 (dock navigation),
P1T-153 (error boundary + shared error surface), P1T-154 (colocation cleanups).

- ~~**`api.ts` is doing five jobs**~~ (P1T-151) — **shipped**. The 1,130-line module is now
  eighteen modules behind `src/api/index.ts` (§5), largest 147 lines. No component import changed,
  and the split was verified as a pure move: the 115 exported names are identical before and after,
  and every non-import line of the original is present unchanged. Type placement (§6) went with it.
- **Ten `fullWidth` tabs in a 360px-minimum dock** (P1T-152). `variant="fullWidth"` divides the panel by ten,
  so at minimum dock width each tab label gets ~36px. The dock grew a tab per agent and the
  navigation shape never changed with it.
- **Error handling is copy-paste** (P1T-153). Eleven independent `error` states, each with its own placement.
  No boundary, so a render throw is a white page.
- ~~**`index.css` knows `#cv-sheet`**~~ (P1T-154) — **shipped**. The print rules moved into the `sx` of
  whoever renders the element (§9); the one global rule left is `body`'s print background. Whether
  those colocated rules actually *win* in a browser was the open half: answered for the CV page by
  `e2e/cv-print.e2e.ts`, and now for the rest of the app by `e2e/print.e2e.ts` (P1T-166). Nothing is
  left open here. It was worth doing rather than assumed — the rail's rules, the mobile drawer's and
  the signed-out bar's all won on the first run, and the **agent dock turned out to have no print rule
  at all**, so its bubble was printing in the corner of every artifact this app produces, a client's
  CV included. `MuiFab` even ships its own `@media print` block insisting the bubble keep its
  background colour (`print-color-adjust: exact`), which is the opposite of what was wanted and is
  what a first-match reader of the emitted CSS had been reporting as the dock's rule.
- ~~**Type placement is split by accident**~~ (P1T-151) — **shipped**. Roster domain types stay in
  `types.ts`; each agent contract now sits in its own agent module beside the hook that returns it.
- **The dock's width lives in `App`** (P1T-154). `useAgentDock` is hoisted because the root element needs the
  width for its push padding — so the dock cannot be moved without moving its state.
