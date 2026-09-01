// The passkey ceremonies (passwordless: no password exists to send). Each one is two round trips —
// the server issues WebAuthn options, the browser drives the authenticator, the server verifies —
// and each stores the returned session token on success, so a completed ceremony leaves the app
// signed in. These are the only mutations in the data layer that touch module-global state.
import { useMutation } from "@tanstack/react-query";
import type { SessionRole } from "../auth/roles";
import { clearSession, getToken, setSession } from "../auth/session";
import { performAuthentication, performRegistration } from "../auth/webauthn";
import { http } from "./http";

export interface AuthSession {
  token: string;
  expiresAt: string;
  userId: string;
  email: string;
  /** Which audience the account belongs to — decides the landing page and the routes on offer. */
  role: SessionRole;
}

interface CeremonyBeginResponse {
  ceremonyId: string;
  optionsJson: string;
}

/**
 * Self-serve signup. Two-step WebAuthn registration: the server returns credential-creation
 * options, the browser drives the authenticator, and the server verifies + creates the account,
 * returning a session token. The control word is the account's recovery secret (P1T-20).
 */
export function useSignup() {
  return useMutation({
    mutationFn: async (input: { email: string; controlWord: string }): Promise<AuthSession> => {
      const begin = (await http.post<CeremonyBeginResponse>("/auth/signup/begin", input)).data;
      const attestation = await performRegistration(begin.optionsJson);
      const session = (
        await http.post<AuthSession>("/auth/signup/complete", {
          ceremonyId: begin.ceremonyId,
          attestation,
        })
      ).data;
      setSession(session.token, session.email, session.role);
      return session;
    },
  });
}

/**
 * Passkey sign-in. The server returns assertion options scoped to the email's registered
 * credentials; the browser signs the challenge and the server verifies it, returning a session
 * token. "No passkey on this device" surfaces as a server error pointing to recovery.
 */
export function useSignin() {
  return useMutation({
    mutationFn: async (input: { email?: string }): Promise<AuthSession> => {
      const begin = (
        await http.post<CeremonyBeginResponse>("/auth/signin/begin", {
          email: input.email?.trim() || null,
        })
      ).data;
      const assertion = await performAuthentication(begin.optionsJson);
      const session = (
        await http.post<AuthSession>("/auth/signin/complete", {
          ceremonyId: begin.ceremonyId,
          assertion,
        })
      ).data;
      setSession(session.token, session.email, session.role);
      return session;
    },
  });
}

/**
 * Account recovery. Verifies email + control word, then registers a NEW passkey for the existing
 * account (the old device's passkey is left intact). Signs the user in on success.
 */
export function useRecover() {
  return useMutation({
    mutationFn: async (input: { email: string; controlWord: string }): Promise<AuthSession> => {
      const begin = (await http.post<CeremonyBeginResponse>("/auth/recover/begin", input)).data;
      const attestation = await performRegistration(begin.optionsJson);
      const session = (
        await http.post<AuthSession>("/auth/recover/complete", {
          ceremonyId: begin.ceremonyId,
          attestation,
        })
      ).data;
      setSession(session.token, session.email, session.role);
      return session;
    },
  });
}

/** Clears the local session. Returns true if a session was present. */
export function signOut(): boolean {
  const had = getToken() !== null;
  clearSession();
  return had;
}

/** Whether a session token is currently stored (not a validity check). */
export function isSignedIn(): boolean {
  return getToken() !== null;
}
