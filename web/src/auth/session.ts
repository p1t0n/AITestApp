import { isSessionRole, type SessionRole } from "./roles";

// Session token storage + a tiny subscription so React can react to sign-in/out. localStorage is
// the source of truth (the axios interceptor reads it); listeners are notified on every change,
// including from other tabs via the storage event.
const TOKEN_KEY = "em.session.token";
// Who the session belongs to. Stored beside the token because the server already returns it from
// every ceremony and the value was simply being dropped — the rail's user block (P1T-161) is the
// first thing that needs it. Presentation only: the token is what authorises, and the server
// remains the authority on both.
const EMAIL_KEY = "em.session.email";
// Which audience the session belongs to. Stored beside the token for the same reason as the email:
// the server returns it from every ceremony, the router needs it on the very first render, and
// decoding the JWT in the browser to find it would be a second source of truth. Presentation only.
const ROLE_KEY = "em.session.role";

const listeners = new Set<() => void>();

function notify(): void {
  for (const l of listeners) l();
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

/** The email of the signed-in user, or `null` — including for a session stored before P1T-161. */
export function getEmail(): string | null {
  return localStorage.getItem(EMAIL_KEY);
}

/**
 * The signed-in user's role, or `null` — including for a session stored before P1T-181. A session
 * with no role is not usable: its token predates the role and token-version claims, so the server
 * refuses it too. Callers send it back to the gate rather than guessing an audience.
 */
export function getRole(): SessionRole | null {
  const stored = localStorage.getItem(ROLE_KEY);
  return isSessionRole(stored) ? stored : null;
}

export function setSession(token: string, email?: string | null, role?: SessionRole | null): void {
  localStorage.setItem(TOKEN_KEY, token);
  if (email) localStorage.setItem(EMAIL_KEY, email);
  else localStorage.removeItem(EMAIL_KEY);
  if (role) localStorage.setItem(ROLE_KEY, role);
  else localStorage.removeItem(ROLE_KEY);
  notify();
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(EMAIL_KEY);
  localStorage.removeItem(ROLE_KEY);
  notify();
}

/** Subscribe to session changes (for useSyncExternalStore). Returns an unsubscribe fn. */
export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  // Cross-tab: another tab signing in/out writes the same keys.
  const onStorage = (e: StorageEvent) => {
    if (e.key === TOKEN_KEY || e.key === EMAIL_KEY || e.key === ROLE_KEY) listener();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}
