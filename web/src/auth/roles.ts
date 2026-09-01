// The two audiences (P1T-181). A Service Manager is staff — the roster, the catalog, user
// administration; an Expert is the person a CV is about and reaches their own data only.
//
// The names are the server's own enum names, because they arrive as claim values in the session
// response and are compared, not translated. The server re-decides every request from the token —
// nothing here is a security boundary, it only decides which chrome and which route a session gets.
export type SessionRole = "ServiceManager" | "Expert";

const ROLES: readonly SessionRole[] = ["ServiceManager", "Expert"];

/** Narrows a stored or server-sent string to a role, so an unknown value never leaks into routing. */
export function isSessionRole(value: string | null | undefined): value is SessionRole {
  return value !== null && value !== undefined && (ROLES as readonly string[]).includes(value);
}

/**
 * Where a session belongs when it lands, and where it is sent back to after asking for a route it
 * cannot have. Never `/signin`: bouncing a signed-in person to the gate reads as "you are signed
 * out", which is both wrong and a dead end — they have no second account to sign in with.
 */
export function landingFor(role: SessionRole): string {
  return role === "Expert" ? "/me" : "/";
}
