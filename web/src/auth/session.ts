// Session token storage + a tiny subscription so React can react to sign-in/out. localStorage is
// the source of truth (the axios interceptor reads it); listeners are notified on every change,
// including from other tabs via the storage event.
const TOKEN_KEY = "em.session.token";
// Who the session belongs to. Stored beside the token because the server already returns it from
// every ceremony and the value was simply being dropped — the rail's user block (P1T-161) is the
// first thing that needs it. Presentation only: the token is what authorises, and the server
// remains the authority on both.
const EMAIL_KEY = "em.session.email";

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

export function setSession(token: string, email?: string | null): void {
  localStorage.setItem(TOKEN_KEY, token);
  if (email) localStorage.setItem(EMAIL_KEY, email);
  else localStorage.removeItem(EMAIL_KEY);
  notify();
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(EMAIL_KEY);
  notify();
}

/** Subscribe to session changes (for useSyncExternalStore). Returns an unsubscribe fn. */
export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  // Cross-tab: another tab signing in/out writes the same keys.
  const onStorage = (e: StorageEvent) => {
    if (e.key === TOKEN_KEY || e.key === EMAIL_KEY) listener();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}
