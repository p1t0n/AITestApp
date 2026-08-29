# Server-side CV render (P1T-139)

Until this slice the CV existed only as a React page the user printed from the browser. That is fine
for a person looking at a screen and useless for everything else: a background worker, a Handoff
Package that wants the candidate's CV attached, a batch export, an agent-driven download. This
records how the headless path is built and why it is built that way.

## Shape

```
EmployeeDetailDto ──CvService.Build──▶ CvDto ──ICvPdfRenderer.Render──▶ byte[]
                     (Application)             (Application seam,
                                                Infrastructure impl)
```

* **`ICvPdfRenderer`** — `api/Application/Cv/ICvPdfRenderer.cs`. One method, `byte[] Render(CvDto)`.
  The seam sits in Application so REST, MCP and any future agent path share one renderer instead of
  each growing its own; it also keeps the Web layer a thin adapter, as invariant 7 requires.
* **`CvPdfRenderer`** — `api/Infrastructure/Documents/CvPdfRenderer.cs`. QuestPDF implementation,
  registered as a singleton (stateless, thread-safe).
* **`CvPdfFileName`** — beside the seam, so the controller does no string work of its own.
* **`GET /api/employees/{id}/cv.pdf`** — builds the CV Projection, renders, returns
  `application/pdf` with a `Content-Disposition` filename.
* **SPA** — `useDownloadCvPdf` in `web/src/api.ts` fetches the PDF as a blob through axios (so the
  session token rides along; a plain `<a href>` would hit the endpoint unauthenticated) and hands it
  to the browser as a download. The Print button stays where it was.

## Decisions

### QuestPDF, not a headless browser

The obvious alternative was rendering the existing SPA page with headless Chromium (Playwright,
Puppeteer) and printing it to PDF. Rejected:

* It puts a browser process — a big one — into the deployment, and this project already runs five
  processes. A render would then need the SPA up and a session to authenticate with, which turns a
  pure function into a distributed call.
* It could not run from a worker or an agent path without dragging the whole front end along.
* Layout would follow whatever the print stylesheet happened to do that day.

QuestPDF is a C# layout API: no browser, no external process, no network, and it embeds its own
fonts, so the render is identical on a laptop and in a container with no system fonts installed.
The cost is that the PDF layout is a second implementation of the CV's look, maintained beside the
React one. That is accepted — they share the CV Projection, so they can differ in styling but never
in content.

### Licence

QuestPDF is dual-licensed: **Community** (free, for organisations under $1M annual revenue) and
paid tiers above that. This project is a POC at the free-tier bar, so the Community licence applies
and is set explicitly in the renderer's static constructor. No licence key enters the repo or the
environment. If this ever ships commercially at scale, that line is where the decision surfaces.

### Determinism

`DocumentMetadata.CreationDate` / `ModifiedDate` are pinned to the Unix epoch. QuestPDF otherwise
stamps `DateTime.Now` into every document, which would make two renders of the same CV differ byte
for byte and rule out caching or diffing the output. A test holds this.

### The photo is not fetched

`CvDto.PhotoUrl` is deliberately ignored. Fetching it would put a network call — and a failure mode
that would have to degrade — inside a render that is otherwise a pure projection. A test asserts the
render is byte-identical with and without a photo URL, so this stays a decision rather than an
oversight. Re-opening it means deciding first where the fetch happens (before the render, with the
bytes passed in) and what an unreachable photo does to the document.

### Empty CVs

A freshly-ingested draft can have nothing on it but a name. QuestPDF rejects an empty container
outright rather than drawing a blank, so the body emits an explicit "No CV content recorded for this
employee yet." line when every optional section is empty.

## Tests

`tests/Application.Tests/CvPdfRendererTests.cs`. There is no golden PDF to diff against, so the
tests hold what actually breaks: a real `%PDF-` document comes out of a full CV; a sparse CV renders
without throwing; two renders of the same input are byte-identical; the photo URL changes nothing;
and the filename slug folds accents, drops non-ASCII, and falls back to `cv.pdf` for a name that
folds away entirely.

## Not covered here

* Photo rendering (above).
* Styling or template knobs — one fixed layout.
* Page-break tuning for very long CVs. QuestPDF flows and paginates; nothing pins where a long
  experience list breaks.
