# Research: does MAF 1.22 offer a chat-history store worth adopting? (EXP-27)

> **Status (2026-09-25):** research only, nothing built. **Recommendation: skip MAF's session
> persistence.** If Roster Q&A threads have to survive a restart, use a plain EF Core table of
> addressable turn rows. The existing explicit-history seam in `RosterQaAgent` stays. A custom
> `ChatHistoryProvider` over that table is possible on stable API, but it buys nothing today and
> gets in the way of the Capture-Verify retry (§4).

Sources, in order of authority:

- **What ships:** the package XML docs at
  `~/.nuget/packages/microsoft.agents.ai{,.abstractions}/1.22.0/lib/net10.0/*.xml`. Member names below
  are cited as `T:`/`M:`/`P:` ids from those files.
- **Stability:** read from the compiled DLLs. A `System.Reflection.Metadata` walk collected every
  `[Experimental]` attribute on the 1.22.0 `Microsoft.Agents.AI.dll` and
  `Microsoft.Agents.AI.Abstractions.dll`. The XML docs do not carry attributes, so they cannot answer
  this.
- **Behaviour:** source at the release tag,
  <https://github.com/microsoft/agent-framework/tree/dotnet-1.22.0/dotnet/src>.
- **Release notes:** <https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.22.0>.
- **Conceptual docs:** <https://learn.microsoft.com/en-us/agent-framework/agents/conversations/storage>
  (updated 2026-08-25). Its C# snippets have drifted from 1.22 (see §6).

## 1. What exists in 1.22, and how stable it is

| Type / member | Package | Stability in 1.22.0 | What it is |
| --- | --- | --- | --- |
| `T:Microsoft.Agents.AI.AgentSession`, `P:…AgentSession.StateBag` | Abstractions | **stable** | The per-conversation state object. The doc calls it the "base abstraction for all agent threads". |
| `T:Microsoft.Agents.AI.AgentSessionStateBag` | Abstractions | **stable** | A thread-safe key → JSON value bag, serialized with the session. |
| `M:…AIAgent.SerializeSessionAsync(…)` / `M:…AIAgent.DeserializeSessionAsync(JsonElement,…)` | Abstractions | **stable** | Turns a session into one `JsonElement` and back. |
| `T:Microsoft.Agents.AI.ChatClientAgentSession` (+ `P:…ConversationId`) | AI | **stable** | `ChatClientAgent`'s session: a `ConversationId` for service-stored history plus the state bag. |
| `T:Microsoft.Agents.AI.ChatHistoryProvider` | Abstractions | **stable** (the `InvokingContext`/`InvokedContext` **constructors** are `MAAI001`) | The extension point for loading and storing history around a run. |
| `T:Microsoft.Agents.AI.InMemoryChatHistoryProvider` (+ `…Options`) | Abstractions | **stable** | The default provider. It keeps `List<ChatMessage>` inside the session's `StateBag`. |
| `T:Microsoft.Agents.AI.ProviderSessionState\`1` | Abstractions | **stable** | A helper for typed provider state in the `StateBag`. |
| `P:…ChatClientAgentOptions.ChatHistoryProvider`, `P:…RequirePerServiceCallChatHistoryPersistence` | AI | **stable** | Where a provider plugs in, and the per-model-call persistence switch. |
| `T:Microsoft.Agents.AI.PerServiceCallChatHistoryPersistingChatClient` | AI | **stable** | A decorator that persists after every model call inside the tool loop. |
| `T:Microsoft.Agents.AI.AgentSessionStore`, `T:…AgentSessionStoreKey` | Abstractions | **`[Experimental("MAAI001")]`** | The contract for storing whole serialized sessions. |
| `T:Microsoft.Agents.AI.DelegatingAgentSessionStore` | AI | **`[Experimental("MAAI001")]`** | A decorator base for session stores. |
| `T:Microsoft.Agents.AI.Compaction.*` (sliding window, truncation, summarization, …) | AI | **`[Experimental("MAAI001")]`** | History compaction strategies. |
| `T:Microsoft.Agents.AI.ChatHistoryMemoryProvider` | AI | stable | Vector-store memory, not a history store. It needs a `VectorStore`, which is out of scope here. |

Four facts shape the rest of this note:

- **The shipped packages contain no concrete `AgentSessionStore`.** Only the abstract base and
  `DelegatingAgentSessionStore` ship in `Microsoft.Agents.AI{,.Abstractions}`. The implementations
  live in hosting packages the repo does not reference:
  - `InMemoryAgentSessionStore` and `NoopAgentSessionStore` in `Microsoft.Agents.AI.Hosting`;
  - `AzureBlobAgentSessionStore` in `Hosting.AzureStorage`;
  - `FoundryAgentSessionStore` in `Foundry.Hosting`.

  Every one of them is also `MAAI001`.
- **The session-store contract has just moved.** The 1.22.0 release notes list *"[PREVIEW BREAKING]
  Promote `AgentSessionStore` into Agents.AI.Abstractions"* (PR #7991). It is a contract still in
  motion.
- **The contract has no delete, list or query.** `AgentSessionStore` exposes `SaveSessionAsync`,
  `GetSessionAsync`, `GetOrCreateSessionAsync` and `GetService`, and nothing else
  (`M:Microsoft.Agents.AI.AgentSessionStore.*`).
- **Persistent history providers exist, but not in our packages.** `CosmosChatHistoryProvider` and
  `ValkeyChatHistoryProvider` ship in their own packages. There is no EF Core or Postgres provider.

## 2. What gets serialized, and whether tool-call intermediates can be kept out

**The session blob.** `ChatClientAgentSession.Serialize` writes the whole object with
`JsonSerializer.SerializeToElement(this, …)`: the `ConversationId` and the entire `StateBag`
(`ChatClientAgentSession.cs`). With the default provider, the bag holds
`InMemoryChatHistoryProvider.State.Messages` under the key `"InMemoryChatHistoryProvider"`. That is the
full `ChatMessage` list: roles, every `AIContent` item, and `AdditionalProperties`. MAF's own docs are
blunt about the result: *"Serialized sessions may contain conversation content, session identifiers,
and other potentially sensitive data including PII"* (`M:…AIAgent.SerializeSessionAsync`). The storage
page adds: *"Treat `AgentSession` as an opaque state object and restore it with the same
agent/provider configuration that created it."*

**What the default provider stores.** `InMemoryChatHistoryProvider.StoreChatHistoryAsync` appends
`RequestMessages.Concat(ResponseMessages)`.

- **Request side:** the default filter drops only messages that came from history
  (`AgentRequestMessageSourceType.ChatHistory`). So the Capture-Verify retry instruction would be
  stored like any user message.
- **Response side:** the default is a **no-op filter that includes all response messages**
  (`M:Microsoft.Agents.AI.ChatHistoryProvider.#ctor(…)`,
  `P:…InMemoryChatHistoryProviderOptions.StorageInputResponseMessageFilter`). A `ChatClientAgent`
  response carries every intermediate message the `FunctionInvokingChatClient` loop produced. That
  includes each `FunctionCallContent` and each `FunctionResultContent`, and for us a
  `FunctionResultContent` is a raw MCP payload: `cv_get` JSON, search snippets.

**Tool intermediates can be excluded.** Pass a `storeInputResponseMessageFilter` to the
`ChatHistoryProvider` constructor, or set `StorageInputResponseMessageFilter` on
`InMemoryChatHistoryProviderOptions`. A filter that keeps only the final assistant text reproduces
what `RosterQaThreadStore.Append` does today. It is opt-in, though. The default stores everything, so
getting it wrong fails open, towards storing more personal data.

`RequirePerServiceCallChatHistoryPersistence` goes the other way: it persists after **every** model
call inside the tool loop (`T:…PerServiceCallChatHistoryPersistingChatClient`). It is not what we want.

## 3. How it would plug into `RosterQaAgent` / `RosterQaThreadStore`

**What the code does today.**

- `RosterQaAgent.AskAsync(question, history, ct)` builds `history + question` as the **input
  messages** and runs it on `agent.CreateSessionAsync()`, a fresh session every time. The
  Capture-Verify retry runs on a second fresh session.
- `RosterQaThreadStore` owns:
  - the per-user thread ownership check;
  - the 30-minute sliding TTL;
  - the 20-thread LRU cap;
  - a 10-turn window of **final question/answer text only**.

  The endpoint in `Program.cs` calls `Resolve`, then `AskAsync`, then `Append`.

**One thing is already true without anybody choosing it.** `ChatClientAgent`'s constructor does
`this.ChatHistoryProvider = options?.ChatHistoryProvider ?? new InMemoryChatHistoryProvider();`
(`ChatClientAgent.cs`). So every Roster Q&A run already fills a default provider, tool intermediates
included, inside an ephemeral session that is then dropped. That is harmless because the session dies
with the request. It is also the reason not to start persisting sessions naively.

**Adoption path A: persist whole sessions** with `SerializeSessionAsync` behind an
`AgentSessionStore`. It would replace `Resolve`/`Append` with `GetOrCreateSessionAsync`/`SaveSessionAsync`.

- There is no concrete store to use, so we would write one on `MAAI001` API that 1.22 has just
  broken.
- The TTL, the LRU cap and the ownership check still have to be ours. `AgentSessionStoreKey`
  partitions help with ownership, but nothing expires or evicts anything.
- The stored value is the opaque blob from §2.

**Adoption path B: a custom `ChatHistoryProvider` over our own table**, set through
`ChatClientAgentOptions.ChatHistoryProvider`. The provider keeps only the thread id in the session
(`ProviderSessionState<T>`), and `ProvideChatHistoryAsync`/`StoreChatHistoryAsync` read and write rows.
This is the path MAF's docs recommend for *"database/Redis/blob-backed history"*. The API is stable,
but the change is structural:

- `AskAsync` would stop taking `history`.
- The retry would have to run on the **same** session to see the history. The provider would then
  record the ungrounded first answer and the retry instruction, unless we override `InvokedCoreAsync`
  or filter by source.
- The provider sees one run at a time, but "a turn" in Roster Q&A is attempt plus retry, decided in
  `AskAsync`. The endpoint's single `Append` after `AskAsync` has returned already models that
  exactly.
- The per-user ownership check and the TTL still live in our code: a provider has no user context
  beyond what we put in the session.

**Adoption path C (no MAF persistence):** keep `RosterQaAgent` as written. Swap the storage inside
`RosterQaThreadStore` from a `ConcurrentDictionary` to EF behind the same `Resolve`/`Append` shape, like
`StaffingProposalStore`, which already uses `IAppDbContext` in the Agents host. Nothing in the agent
changes, and the retry semantics stay exactly as they are.

## 4. GDPR: does the storage shape fight erasure and scrubbing?

`manuals/personal-data-and-erasure.md` sets three bars:

- every store is declared in `PersonalDataDeclaration`;
- the model walk flags any entity carrying a `*UserId`/`*ExpertId`;
- erasure is either an FK cascade or a **field-level scrub of addressable places**.

Where JSON cannot be avoided, the precedent is `HandoffPackageScrub`, which walks six named paths and
proves the round-trip with a typed test.

**Path A fails that bar.** A serialized session is one JSON blob per thread, with messages nested under
a `StateBag` key whose layout MAF owns and has changed recently. The blob holds:

- the Service Manager's questions: their data, deletable by cascade on `UserId`, which is fine;
- third-party expert data, which is not. Answers name experts with their ids, and with default
  filters the `FunctionResultContent` payloads (whole `cv_get` results) would be in it too.

Erasing an **expert** would then mean walking every other user's blobs for their name, id and CV
fragments. Nothing in `AgentSessionStore` supports that: there is no enumerate and no delete. MAF also
tells us to treat the blob as opaque, which rules out editing it in place.

**Path B/C rows are addressable.** A `RosterQaTurn(ThreadId, UserId FK cascade, Question, Answer,
CreatedAt)` table:

- is caught by the existing declaration audit;
- cascades with the account;
- can be swept by `ErasureTests`' string-column scan.

Free-text answers that name an erased expert are still a residue, and a row shape does not make that
problem go away. The honest control is **short retention**: keep the current 30-minute or 10-turn
bound, or a days-scale expiry via `IRetentionErasure`. The other half is **never storing tool
payloads**, so an answer holds a name and an id, not a CV.

## 5. Recommendation: skip

**Do not adopt `AgentSessionStore` or session serialization.**

- It is `MAAI001` and just broke in 1.22.
- It ships no store we could use.
- It offers no delete.
- It stores an opaque blob that, by default, includes tool payloads full of third-party CV data.

**Do not add a custom `ChatHistoryProvider` either, for now.** It is stable and would work, but it moves
turn assembly out of `AskAsync`, where the Capture-Verify retry needs it, and gains us nothing
`RosterQaThreadStore` does not already do.

**If durability is wanted:**

1. Back `RosterQaThreadStore` with an EF table of addressable turn rows: question and answer text only,
   `UserId` FK with cascade.
2. Declare the table in `PersonalDataDeclaration`.
3. Give it a retention bound.
4. Leave `RosterQaAgent` untouched.

**Revisit** if the conversation ever moves to service-managed history (`ChatClientAgentSession.ConversationId`
on a Responses/Conversations backend), or if MAF promotes `AgentSessionStore` to stable with delete
semantics.

## 6. Doc drift noticed (for whoever reads Learn next)

The Learn storage page's C# does not compile against 1.22:

- It overrides `public override string StateKey`, but 1.22 has
  `IReadOnlyList<string> StateKeys` (`P:Microsoft.Agents.AI.ChatHistoryProvider.StateKeys`).
- It calls `agent.SerializeSession(session)`, but 1.22 has `SerializeSessionAsync`.

Trust the XML docs and the tag source over the page.
