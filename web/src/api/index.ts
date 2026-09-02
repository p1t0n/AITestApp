// The data layer's single import site. Every component imports from "…/api" and nothing else —
// no component knows a URL, a query key, or which of the two backends serves a call.
//
// That property is the reason this barrel exists (P1T-151): the modules behind it are split by
// domain so each one stays readable, while the import path components use is unchanged.
//
//   http               the two axios clients, the token interceptor, apiErrorMessage
//   auth               the three passkey ceremonies + the local session helpers
//   experts          the roster aggregate: list, detail, CV, PDF, promote
//   expertChildren   skills, availability, languages, qualifications, experiences
//   catalog            the skill-catalog tree
//   notice             the versioned transparency notice and its acknowledgment
//   users              user administration and cap overrides
//   claims             the claim queue, claim codes, and revocation
//   visibility         the Expert's own pause control
//   erasure            deleting yourself: account and record together
//   transparency       what we hold on you, and the portable copy
//   contests           contesting an automated score, and the review of it
//   download           turning a response into a saved file
//   agents/*           one module per agent surface, each DTO beside the hook that returns it
//
// Roster domain types stay in src/types.ts; agent contracts live beside their hooks.
export * from "./http";
export * from "./auth";
export * from "./experts";
export * from "./expertChildren";
export * from "./catalog";
export * from "./notice";
export * from "./users";
export * from "./claims";
export * from "./visibility";
export * from "./erasure";
export * from "./transparency";
export * from "./contests";

export * from "./agents/usage";
export * from "./agents/shared";
export * from "./agents/rosterQa";
export * from "./agents/tailoring";
export * from "./agents/match";
export * from "./agents/bench";
export * from "./agents/interviewKit";
export * from "./agents/shortlist";
export * from "./agents/staffing";
export * from "./agents/proposals";
export * from "./agents/rosterScan";
export * from "./agents/ingestion";
