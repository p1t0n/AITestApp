# SPA editing for languages, qualifications and experiences (P1T-142)

The API has supported these three children since the domain slice. The SPA rendered all three
read-only: chips and `<ul>`s with no way to add, edit or remove, and `web/src/api.ts` had no client
function for any of them. Availability and skills had inline add/delete; these did not. This records
the slice that closes it.

The gap was not only a missing feature. `ExperiencesController` had no `[ApiController]`, so MVC did
not infer `[FromBody]` and every JSON `POST`/`PUT` bound an empty DTO and answered 400 — dead from
the day it was written. Nothing noticed for as long as nothing in the SPA called it (P1T-140 found
it from the other side). A UI that never exercises an endpoint is how an endpoint stays broken.

## Shape

```
web/src/
  types.ts                     SaveSpokenLanguage / SaveQualification / SaveExperience(+SaveAchievement)
  api.ts                       add/update/delete per child, invalidating ["employees", id]
  pages/
    LanguageFormDialog.tsx     language + level
    QualificationFormDialog.tsx  the Degree half or the Certification half, never both
    ExperienceFormDialog.tsx   scalars + the bullet editor + the catalog skill picker
    EmployeeDetailPage.tsx     Add / Edit / Delete affordances on the three sections
    ChildFormDialogs.test.tsx  11 component tests
  e2e/employee-children.e2e.ts three browser journeys against a real API
```

## Decisions

**The dialog is mounted only while a row is being edited.** The `EmployeeFormDialog` pattern seeds
its state with `useState({ ...empty, ...initial })`, which reads `initial` on first render and never
again. That is invisible for a single employee-edit dialog and wrong for a list: a dialog kept
mounted across two rows shows the first row's values for the second. Conditional rendering
(`{languageEdit && <LanguageFormDialog … />}`) makes mounting the thing that loads the form, so the
existing pattern stays usable without a `useEffect` that re-syncs state behind the user's back.

**The experience form is one nested editor, not three forms.** `SaveExperienceDto` carries its
achievements and its skill ids, and the server replaces both lists on every save — see *Child
Collection Replace* in `CONTEXT.md`. Splitting bullets onto their own endpoints would have meant
inventing endpoints the domain does not have. The consequence to keep in mind: nothing is written
until Save, and Save is a replace, so a concurrent editor of the same experience loses.

**Bullet order comes from position, never from the user.** The form renumbers `order` from array
index on save, so the interaction is "move this up" and the number is a consequence. Blank bullets
are dropped rather than sent: an empty row is someone who clicked Add and changed their mind, not an
error worth a round trip to report.

**The qualification form shows one half at a time.** One record covers a Degree (institution, field,
study dates) and a Certification (issuer, credential id, issue/expiry). Rendering all ten fields
would ask the user to ignore half of them. The type select chooses the half, and the unused half is
nulled *on save* — so a record re-typed from Degree to Certification does not carry its old
institution along silently.

**No client-side validation.** FluentValidation in the Application layer is the only validator, so
REST and MCP agree (a product invariant). The forms surface what the server says through
`apiErrorMessage` and keep the dialog open with the input intact. Duplicating the rules in
TypeScript would create a second source of truth that drifts.

**Skills are picked from the catalog, never typed.** `useSkills` feeds an MUI `Autocomplete`, the
same shape the agent tabs use. An experience skill is a link to a catalog row; free text would
invent skills outside the catalog the whole RAG projection is built on.

## Verifying the tests can fail

Both layers were broken deliberately once, since a green suite proves nothing on its own:

* Removing the blank-bullet filter fails
  `renumbers bullets from their position after a move, and drops blank ones`.
* Sending `achievements: []` from the form fails the e2e
  `an experience with bullets and a skill is written, edited, and reaches the CV` — at the CV
  assertion, which is the far end of the path the dead controller used to break.

## Worth revisiting

* **Availability and employee skills still have no edit.** `PUT /api/availability/{id}` and
  `PUT /api/employee-skills/{id}` exist and have no caller; the inline add/delete rows predate this
  slice and were left alone. Correcting a typo in either means delete-then-add today.
* **Delete asks nothing.** Removing an experience — bullets and all — happens on one click, matching
  the existing skill/availability chips. `CatalogPage` has a confirm dialog; if a bullet is ever lost
  in anger, that pattern is the answer.
* **Concurrent edits.** The experience PUT is last-writer-wins over the whole child collection.
  Nothing detects that two people opened the same experience. A version/ETag on the experience would,
  and would cost a domain change.
