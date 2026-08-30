# MCP Tool Grants: moving the Tool Allowlist onto the identity

> **Status (2026-08-30):** shipped as P1T-149. Each agent's Keycloak client carries
> `mcp:tool:<name>` scopes; the MCP server narrows `tools/list` to them and refuses `tools/call`
> outside them. P1T-146's client-side Tool Allowlist stays — it is now the local echo of the
> grant, not the boundary. Vocabulary: `CONTEXT.md` → **Tool Grant**, **Tool Allowlist**.
> Cost context: `manuals/agent-cost-budgets.md` §3.5.

## 1. What was wrong with the client-side allowlist

P1T-146 shipped a real win and a real gap in the same change.

The win was cost. An unused tool is not free: its schema is part of Baseline Prompt Size, and
Turn Amplification re-sends that on every iteration. roster-qa paid for seven tools it never
called, ten times over — 26% of a 160,220-token run. Narrowing it to 4 of 11 took its Baseline
Prompt Size from 4,409 to 1,873.

The gap was that the narrowing was in the wrong place. `McpToolSource` asks the server for tools
and then discards some of them. Two things follow, and only the second one matters:

- The agent still *pays* for nothing it discarded — the discard happens before the model sees
  anything. So this was never a cost defect.
- The agent was still **entitled** to everything it discarded. Its token carried `mcp:read`,
  which the server reads as "all 11 read tools". A filter you apply to yourself is a convention.
  Delete the filter, or make a call by name without consulting the list, and the whole read
  surface is there.

That second point is what P1T-149 closes. The project's third invariant is *capability is
enforced by the token, not by the prompt* — and "which tools may this identity see" is a
capability question that was being answered by client code.

## 2. The shape

A second axis of scope on the same claim.

| Axis | Scopes | Answers |
| --- | --- | --- |
| Capability | `mcp:read`, `mcp:write`, `mcp:admin` | What *kind* of thing may this caller do? |
| Grant | `mcp:tool:<name>`, e.g. `mcp:tool:cv_get` | *Which* of those tools may it use? |

Two rules, and the second is the load-bearing one:

1. **Grants only ever narrow.** They compose with the capability scopes rather than replacing
   them, so `mcp:tool:employee_delete` on an `mcp:read` token buys nothing — deletes need
   `mcp:admin`, and the tool's own `[Authorize]` policy is still what decides that. A grant says
   "of the tools you may already use, these". It is deliberately a distinct prefix so it can
   never be mistaken for a route to capability.
2. **No grants means no narrowing.** A token carrying none is shown everything its capability
   scopes carry. This is the same rule as an absent Tool Allowlist and it exists for the same
   reason: a forgotten client-scope assignment must not quietly cripple an agent. It is also what
   keeps `cv-manager-mcp` — the interactive PKCE client a person drives — on the whole surface,
   and `cv-manager-e2e` able to exercise all of it. **Narrowing is opt-in.**

## 3. Where it is enforced, and why not on the tool

`McpToolGrants` reads the grants off the caller's principal; `ToolGrantFilters` applies them as
two MCP request filters, registered next to the SDK's own `AddAuthorizationFilters()`:

- `tools/list` — the result is narrowed to the granted tools.
- `tools/call` — an ungranted name is refused before the handler runs.

The obvious alternative was a finer policy on each tool's `[Authorize]` attribute, which the
ticket sketched. It was rejected for one reason: **a per-tool attribute has to be remembered.**
There are 40 tools and the surface grows; the failure mode of forgetting the attribute on tool 41
is that it is silently ungated, which is the worst direction for a security control to fail in. A
filter over the request cannot be forgotten, because it does not know or care how many tools
exist. It also keeps the capability axis exactly as it was — every `[Authorize(Policy = ...)]`
attribute in `api/Mcp/Tools` is untouched by this change.

Both gates must pass, and the filters compose in either order: the list filter narrows whatever
survived the capability check, and a call clears both whichever runs first.

### The refusal is a result, not a fault

An ungranted `tools/call` comes back as a structured tool error — `IsError`, with the code
`forbidden` and a message naming the tool — the same shape as `not_found` / `conflict` /
`validation` from `McpToolErrorMapper`.

This deliberately differs from the SDK's own scope refusal, which throws a protocol error. The
reason is the fourth invariant: *degrade, never 500*. An agent that reads an error result picks
another tool and finishes its run; an agent that takes a protocol fault does not. A tool the
model should not have reached for is a thing to correct, not a thing to die on. Returning rather
than throwing has a second benefit — it makes the behaviour independent of where the filter sits
in the pipeline, so nothing here rests on registration order.

## 4. One fact, three copies, and the chain that holds them together

The grant set now exists in three places, which is a drift hazard worth being explicit about:

| Copy | Where | What it does |
| --- | --- | --- |
| The declaration | `CostFloors.AgentToolAllowlists` | What each agent may see. The Baseline Prompt Size floor measures against it. |
| The identity | `keycloak/realm-export.json` | The `mcp:tool:*` scopes the token actually carries. **The boundary.** |
| The client | `McpAuth:<agent>:Tools` in `api/Agents/appsettings.json` | What `McpToolSource` filters to locally. |

Two tests chain them to the declaration, so the three are provably one set:

- `Agents.Tests/AgentToolAllowlistTests` — the shipped `appsettings.json` matches the declaration.
- `Mcp.Tests/KeycloakToolGrantTests` — the shipped realm matches it too, every granted scope is
  declared as a client scope with `include.in.token.scope`, no grant scope is orphaned in either
  direction, and every `agent-*` client in the realm is narrowed. Deterministic: the realm export
  is JSON on disk, so this needs no Docker and no running Keycloak.

The first edit that breaks the chain is a red test, rather than an agent that silently loses a
tool at runtime or a cost ceiling that stops describing the running system.

### Why the client-side filter stays

It is no longer the boundary, so the question is whether it earns its keep. It does, twice:

- The MCP server runs `SearchIndex` and a database; a developer running the Agents service
  against a stale realm import gets the configured surface rather than the whole one. The
  narrowing degrades to P1T-146's behaviour instead of vanishing.
- It is what the Baseline Prompt Size floor measures against, and that floor runs with no MCP
  server standing behind it at all.

The in-agent per-turn filters stay too, and are a third, different thing: the grant is the outer
bound on the identity, the allowlist is the local echo, and the per-turn filter is which of those
tools *this turn* offers — CV Tailoring genuinely shows `cv_get` on turn one and
`style_exemplar_search` on turn two.

## 5. What was deliberately not done

- **`scopes_supported` was left at the three capability scopes.** RFC 9728 advertises a list, not
  a pattern, so being exhaustive would mean 40 entries in the protected-resource metadata for a
  thing no client negotiates: grants are provisioned on an identity in Keycloak, not requested
  through Dynamic Client Registration. The metadata describes what a client can ask for, and a
  client asks for capability.
- **The agents' token requests were not changed.** The grants are *default* client scopes on each
  agent's Keycloak client, so they ride on every token that identity is issued, requested or not.
  Adding them to the request would have made a stale realm import fail token acquisition outright
  (`invalid_scope`) instead of degrading to an unnarrowed token — a worse failure for a thing
  whose whole design principle is that narrowing is opt-in.
- **Nothing was re-baselined.** No cost ceiling moves: the model is shown exactly the same tools
  it was shown yesterday, because the grant sets are copied from the allowlists. This change buys
  a boundary, not tokens.

## 6. Adding a tool to an agent

1. Add the name to `CostFloors.AgentToolAllowlists` — the declaration moves first.
2. Add `mcp:tool:<name>` to that agent's `defaultClientScopes` in `keycloak/realm-export.json`,
   and declare the client scope itself if no other agent already grants that tool.
3. Add the name to `McpAuth:<agent>:Tools` in `api/Agents/appsettings.json`.
4. Re-run the Baseline Prompt Size floor: the tool's schema is now paid on every iteration, so
   the agent's ceiling moves and the move should be a deliberate line in the diff.

Skipping step 2 is the interesting failure, and it is the one worth knowing the shape of: the
server refuses the call with `forbidden` and the agent degrades rather than crashing, while
`KeycloakToolGrantTests` fails in CI. Loud in the place that can fix it, soft in the place that
cannot.
