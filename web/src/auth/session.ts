// Session token storage + a tiny subscription so React can react to sign-in/out. localStorage is
// the source of truth (the axios interceptor reads it); listeners are notified on every change,
// including from other tabs via the storage event.
const TOKEN_KEY = "em.session.token";

const listeners = new Set<() => void>();

function notify(): void {
  for (const l of listeners) l();
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setSession(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
  notify();
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  notify();
}

/** Subscribe to session changes (for useSyncExternalStore). Returns an unsubscribe fn. */
export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  // Cross-tab: another tab signing in/out writes the same key.
  const onStorage = (e: StorageEvent) => {
    if (e.key === TOKEN_KEY) listener();
  };
  window.addEventListener("storage", onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("storage", onStorage);
  };
}
