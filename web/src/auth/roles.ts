// The two audiences (P1T-181, renamed in P1T-236). An Administrator is staff — the roster, the
// catalog, user administration; a User is the person a CV is about and reaches their own data only.
// "User" is the role, not the account: an Expert is the roster row, and one person is normally both.
//
// The names are the server's own enum names, because they arrive as claim values in the session
// response and are compared, not translated. The server re-decides every request from the token —
// nothing here is a security boundary, it only decides which chrome and which route a session gets.
export type SessionRole = "Administrator" | "User";

const ROLES: readonly SessionRole[] = ["Administrator", "User"];

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
  // Not "/me": that is a redirect, and a landing that bounces is a landing that flickers.
  // My CV is the User's home, and it sends somebody who owns no record on to claim status.
  return role === "User" ? "/me/cv" : "/";
}
