import { useSyncExternalStore } from "react";
import { getEmail, getToken, subscribe } from "./session";

/**
 * Reactive auth state. Re-renders subscribers whenever the session token changes (sign-in,
 * sign-out, or another tab). This is presence-only — it does not validate the token; the server
 * is the authority and returns 401 if it has expired.
 */
export function useIsAuthenticated(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => getToken() !== null,
    () => false,
  );
}

/**
 * The signed-in user's email, reactively — `null` when signed out, and also `null` for a session
 * that predates the app storing it. Every caller therefore has to render the absence.
 */
export function useSessionEmail(): string | null {
  return useSyncExternalStore(
    subscribe,
    getEmail,
    () => null,
  );
}
