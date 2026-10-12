# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Two roles, treated as co-primary (confirmed):

- **Administrator**: staff at a services firm who select Experts for a Job. Pastes a job description, reviews coverage-ranked candidates, approves or rejects staffing Proposals, and administers the Roster, skills catalog and accounts.
- **User / Expert**: a person the Roster holds a CV for. Maintains their own CV, skills, qualifications, experience and availability, and sees what is held about them (privacy page, export, erasure).

Design must serve both evenly; neither is a secondary audience.

## Product Purpose

ExpertToJob manages available Experts (skills, qualifications, experience, time-based availability), renders their CVs, and runs built-in AI agents over the Roster: Roster Q&A, CV Tailoring, Match, JD Shortlist, Interview Kit, Roster Scan, and a streaming staffing pipeline. An MCP server exposes every operation to external agents over the same Application layer. Success: an Administrator reaches a defensible, evidence-backed staffing decision faster, and an Expert's data stays accurate and under their control.

## Positioning

Honest, evidence-linked AI (confirmed). Agents only propose; humans hold write authority and approve once. Evidence quotes are verified verbatim against captured CV data and stripped if unverifiable. Capability is enforced by the MCP token scope, not the prompt. Deterministic facts are composed in code, not written by the model. Failures degrade visibly instead of erroring or faking confidence. GDPR / Art. 22 safeguards are part of the product.

## Operating Context

Passkey-only sign-in (WebAuthn, no passwords). Five processes: Web API, MCP server, Agents host, SPA, Postgres + Keycloak. The SPA talks to `/api` and `/agents` with one bearer token; the agents widget streams over SSE. Free-tier or quota-limited model providers mean degraded modes (keyword fallback for semantic search, paused Roster Scans) are normal states the UI must show honestly.

## Capabilities and Constraints

- Domain language is pinned in `CONTEXT.md`: Expert, Roster (all) vs Bench (offered, Active and not Paused), Job, Administrator, User, Role, Proposal. Use these terms in UI copy; avoid employee, candidate (outside a Job), recruiter, Service Manager.
- Frozen surfaces: 39 `data-testid` hooks, several accessible names (`Sign in`, `Sign out`, `CVs`, `Search`, `Open the agents assistant`, ...) and the rail/dock push contracts are asserted by tests (`web/src/frozenHooks.test.ts`, e2e). Do not rename or add silently. See `manuals/spa-design-system.md` §9.
- No React Context in the SPA; styling goes through the MUI theme layer, with `sx` for spacing and layout only (Override Policy).
- Governing specs: `manuals/spa-architecture.md`, `manuals/spa-design-system.md`.

## Brand Commitments

The neumorphic relief look is binding: `manuals/adr-neumorphic-reskin.md` (accepted 2026-09-03) governs the visual language (dual-shadow depth, amber accent, Plus Jakarta Sans / DM Sans / JetBrains Mono). Any change to it is a replacement decision, not a refinement. Recorded here as a commitment only; the world itself belongs in DESIGN.md.

## Evidence on Hand

Real data: the 500-expert synthetic demo roster (`tools/SeedDemoRoster`, explicit-start `demo-roster` resource). Screenshot tooling exists (`npm run shots`, visual regression via `npm run test:visual`). No real customers, testimonials, or benchmarks exist. Never fabricate Expert data, customers or proof in mocks beyond the synthetic roster.

## Product Principles

1. Agents propose, humans dispose. UI makes the approval moment and its evidence unmistakable.
2. Show the evidence, not just the answer: quotes, provenance, `inferred` badges, ambiguities.
3. Degradation is a visible state, never silent and never a crash.
4. Both roles are first-class; role differences are authorization, not a lesser experience.
5. Use the pinned domain vocabulary consistently in every label and message.

## Accessibility & Inclusion

WCAG AA, asserted rather than eyeballed: contrast floors are tested against token pairs and rendered composites (`theme/tokens.contrast.test.ts`, `theme/components.test.tsx`). Keyboard operable; passkey-only auth must remain accessible. Print behaviour (CV render) is verified in a real browser.
