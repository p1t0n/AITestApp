# ADR: Roster Q&A conversations are durable, owner-only, and inside the privacy machinery

**Status:** accepted, 2026-09-25. Charted as Linear map EXP-23 ("Map: durable Roster Q&A
conversation history, reviewable by its owner"). Its five decision tickets carry the reasoning
behind every section here and are cited by key. No code implements this yet; the build slices are
listed in §9.

## The decision

A Roster Q&A **Conversation** stops being an in-memory thread that dies after 30 idle minutes or a
restart. It becomes a set of rows in Postgres that its owner can review in the agent dock, continue
from any device, and delete. Nobody else can read it, Administrators included. Every stored turn is
personal data from the moment it is written. So the store is declared, scrubbed on erasure, masked on
pause, expired after six months of inactivity, and disclosed (by existence, never by text) in the
Access View of every Expert it touched.

This **reverses** the "lost on restart by design" choice recorded on `RosterQaThreadStore` (P1T-93).
That choice was scoped to a showcase and weighed against MAF's durable/distributed *runtime*
(`manuals/maf-orchestration-primitives.md`). A plain table was never the alternative it rejected.

Scope is Roster Q&A only. The one-shot surfaces (tailoring, match, interview kit, shortlist, bench,
ingestion) produce results, not conversations, and are out of scope. So is search across
conversations.

## 1. Vocabulary

Two domain terms enter `CONTEXT.md` (under *Agent dock*): **Conversation** and **Turn**. The rest of
this document uses them in that sense. Implementation vocabulary stays here:

**Touched Expert**: an Expert whose id appeared in *any* tool result during a turn. This is a
superset of the ones the answer names.

**Replay window**: the last 10 question/answer turns of a conversation, text only, sent ahead of a
new question. It's the same bound `RosterQaThreadStore` applies today.

## 2. Storage: a plain EF table, not MAF sessions (EXP-27)

MAF 1.22 was researched first (`research/maf-chat-history-store` branch,
`manuals/research-maf-chat-history-store.md`):

- `AgentSessionStore` is experimental (MAAI001), was moved in 1.22 with a breaking change, and has
  no delete, list or query.
- A serialized session is one opaque JSON blob. By default it holds tool calls **and results**,
  i.e. other people's raw `cv_get` payloads.
- A blob that must be walked to find an Expert fails the addressable-field bar in
  `personal-data-and-erasure.md`.

So:

- Three entities live in `Domain` and `AppDbContext`, next to `StaffingProposal`, with the schema
  owned by `api/Migrator` like every other table: `RosterQaConversation`, `RosterQaTurn` and
  `RosterQaTurnExpert`.
- The Agents host already writes its own records through `IAppDbContext` and already applies
  `RosterVisibility` (`StaffingProposalStore`, `ScoringJobStore`), so nothing new is plumbed.
- `RosterQaAgent` does not change. It still receives an explicit history list and runs an ephemeral
  session per question.

## 3. What is stored (EXP-25)

**Conversation:** `Id`, `UserId` (owner, cascades with the account), `CreatedAt`, `LastActiveAt`,
`Title`.

- The **title** is the first question, trimmed to about 60 characters at a word boundary.
- There is no model-written title and no rename.

**Turn:** `ConversationId`, `QuestionText`, `AnswerText`, `ModelId`, `Grounded`, `CreatedAt`,
`State` (`Ok` | `Removed`).

- `AnswerText` is exactly what was shown, including the "could not be grounded" note, so a replay
  reads as it does today.
- `ModelId` is the model the provider reported (`AgentReply.ModelId`), not configuration.
- `Grounded` is `CaptureScope.Captured` after the Capture-Verify retry. The dock never parses the
  note out of the text.

**Touched Experts:** one `RosterQaTurnExpert(TurnId, ExpertId)` row per distinct Expert id found in
any tool result during the turn.

- The capture wrapper in `CaptureVerifyGuard` extracts them in code; they are never parsed from
  model text.
- The superset is deliberate. Erasure may scrub a turn that only listed someone in passing, but it
  never misses one where the model saw them and could have quoted them.
- There is no FK to `Expert`: the reference must survive the Expert's erasure until the scrub has
  used it, in the same transaction.

**Not stored:**

- Tool payloads.
- Tokens, tool sequence, latency and trace id. They already live on `AgentUsage` per call, and a
  second copy would be one more thing to erase.
- Failed turns: a model error, a cap reached or a timeout records nothing. Only completed turns are
  appended, as today.

Turns are **append-only inserts**. Two tabs asking into one conversation each append their own turn,
and each question replays whatever 10 turns exist when it starts. `LastActiveAt` is the latest turn's
time.

## 4. Resume (EXP-24)

- Opening a past conversation lets its owner keep asking.
- The replay window is unchanged: the last 10 Q/A turns as text, on the **currently configured**
  model, with no pinning to the model a turn was written by.
- Every turn already forces a fresh tool call (`ChatToolMode.RequireAny`), so a new answer rests on
  today's roster data, not on what an old turn said.
- The 30-minute sliding TTL and the 20-thread cap stop being lifetime rules. There is no age cutoff
  on resume; age is retention's job (§6).
- The request contract of `POST /agents/roster-qa` is unchanged. `threadId` becomes the conversation
  id. An unknown id, or someone else's, starts a fresh conversation, the way an expired thread does
  today.

**Hard rule:** *a replayed turn never carries data about an Expert who has since been erased or
paused.* §5 is how that holds.

## 5. Privacy (EXP-26)

"Restricted" in the map's constraint means **Paused**. No Art. 18 flag exists in code, and
`HiddenAt` read through `RosterVisibility.NotHidden` is the one real stop-processing state.

### Expert erasure: turn-level scrub

Inside `ErasureService.EraseAsync`, in the same `SaveChanges`:

- Every turn with a `RosterQaTurnExpert` row for the erased Expert has `QuestionText` and `AnswerText`
  set to empty and `State = Removed`, and those `RosterQaTurnExpert` rows are deleted.
- Every turn, and every conversation title, whose text contains the Expert's **full name**
  (case-insensitive) is scrubbed the same way, alongside the id match. That is what lets
  `ErasureTests.After_erasure_nothing_personal_survives_in_any_declared_store` hold for these stores.
- A conversation whose first turn is scrubbed gets the title "Conversation from <date>".
- Timestamps and the remaining turns stay. The conversation is still the owner's.

Nicknames and misspellings in typed questions are a residual risk, recorded in the DPIA (R6/R13).
Today's proposal free text has the same gap.

### Owner erasure

`UserId` cascades conversation → turns → touched rows, exactly as `AgentUsage` does.

### Pause: read-time mask, no rewrite

- Turns with a touched Expert who is currently paused are **left out of the replay window** and
  **shown masked in review** (`State` reads as Hidden at query time; it is never stored).
- Unpausing restores both, at no cost.
- The test goes through `RosterVisibility.NotHidden` and nowhere else, so `VisibilitySeamTests` stays
  green.

### Owner delete

- Delete one conversation, or all of the caller's.
- Immediate and hard, with no control word: the data is the caller's and the act is low-stakes.
- The routes take no user id, only the caller's own, so they can't delete anyone else's data.

### Access View

- An Expert's `GET /api/me/access` gains one line: "Roster Q&A answers referenced you N times
  between <first> and <last> (model: …)", counted from `RosterQaTurnExpert`.
- **Existence, never text.** A turn's answer can be mostly about other people (a search listing
  twenty of them), and handing it over would disclose third parties' data (Art. 15(4)).
- This is a legal judgement and is flagged for the DPO in the DPIA. It is also what keeps
  `TransparencyTests.Every_store_the_scrub_reaches_is_visible_in_the_access_view` honest for this
  store.
- No staff Access View or Export is added; none exists today. The owner's own access is the dock.

### Personal-Data Declaration

- `RosterQaConversation` is **Scrub** (`Title`).
- `RosterQaTurn` is **Scrub** (`QuestionText`, `AnswerText`).
- `RosterQaTurnExpert` is **Delete**.
- Each gets its reason written next to it, and `PersonalDataDeclarationTests` gates all three.

### Special-category data in typed questions

No content filter. It's covered by R3 and the standing ban on inferring protected characteristics,
and bounded by retention and owner delete.

## 6. Retention

- A conversation is **hard-deleted 6 months after its `LastActiveAt`**. That matches the
  unclaimed-record period, and transcripts quote CVs, so indefinite retention would not survive the
  DPIA.
- This is a conversation age rule, not part of any Expert's **Retention Clock**, which agent activity
  still never moves.
- It runs as a daily `ConversationRetentionWorker` in the **Agents host** (which owns the data),
  **on by default**. Deleting stale transcripts is the safe direction.
- It deliberately does not mirror `RetentionWorker`, which is off by default because it *erases
  people*. A six-month promise that is off by default would be a DPIA claim nobody keeps.

## 7. The dock (EXP-28)

Prototype: branch `prototype/exp-28-conversation-history`, three variants with screenshots,
throwaway.

- **Header:** the Roster Q&A surface header shows the current title (ellipsized), a
  "New conversation" button, and a "Conversation history" button.
- **Drawer:** history opens as a drawer **over** the transcript, grouped Today / This week / Older,
  so the transcript never loses width in a narrow dock. The split-list variant is dropped: titles cut
  to about 12 characters at the default width.
- **Retention in the UI:** one footer sentence ("Conversations are deleted 6 months after their last
  activity."), plus a "disappears in N d" hint in a conversation's last 14 days.
- **Delete:**
  - per row, shown on hover or focus, with no confirmation;
  - "Delete all conversations" in the drawer footer, which asks for confirmation.
- **Turns:**
  - Removed and Hidden turns are one muted italic line in place of both bubbles, with no detail.
  - An ungrounded answer gets a dashed warning edge and a "Not grounded" chip.
  - Each answer carries `<model> · <relative time>`.
- **Accessible names:** "Conversation history", "New conversation", "Close history",
  `Delete conversation "<title>"`, "Delete all conversations". No new `data-testid` unless
  `frozenHooks.test.ts` moves with it.
- **Dependencies:** this rides on the sibling issues "Keep dock surfaces mounted across Token Ledger
  and surface switches" (surface state survives the ledger) and "Show the chat model in the agent
  dock" (`modelId` on the Roster Q&A reply).

## 8. What this does not do

- No search across conversations.
- No rename.
- No sharing, and no staff oversight of anyone else's conversations. Oversight of agent work already
  runs through Proposals and Handoff Packages.
- No Past Runs for the one-shot surfaces.
- No content filtering of questions.
- No model-written summaries.

## 9. Build slices

Each slice is a `ready-for-agent` Linear issue with native `blockedBy`:

1. **Privacy-complete conversation store.** Entities, migration, declaration, erasure scrub, account
   cascade, Access View line. No write path.
2. **Roster Q&A persists and resumes.** Touched-id capture; the durable store replaces
   `RosterQaThreadStore`; replay excludes paused Experts. Blocked by 1.
3. **History API.** List, get (with mask and state), delete one, delete all. Blocked by 2.
4. **Dock history UI.** Blocked by 3 and by both sibling dock issues.
5. **Conversation retention worker.** Blocked by 1.
