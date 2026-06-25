// Session token storage. The app is gated app-wide in a later issue (P1T-22); for now this just
// persists the JWT returned by signup/signin so authenticated API calls carry it.
const TOKEN_KEY = "em.session.token";

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setSession(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
}
