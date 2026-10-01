// Transport for the two backends the SPA talks to. Nothing above this module knows a base URL,
// and nothing below it knows about React Query.
import axios from "axios";
import { expireSession, getToken } from "../auth/session";

export const http = axios.create({ baseURL: "/api" });

// Roster Q&A agent lives on its own sibling service (proxied at /agents), not the CRUD API.
export const agentHttp = axios.create({ baseURL: "/agents" });

// Attach the session token (if any) to every request on both services. The token is issued by the
// Web host and validated by both Web and Agents (shared signing key).
for (const client of [http, agentHttp]) {
  client.interceptors.request.use((config) => {
    const token = getToken();
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  });
  // A 401 means the token is expired or refused — there is no refresh token to trade in, so the
  // session ends and the router sends the user to sign in again.
  client.interceptors.response.use(undefined, (error: unknown) => {
    if (axios.isAxiosError(error) && error.response?.status === 401) {
      const sent = error.config?.headers?.Authorization;
      expireSession(typeof sent === "string" ? sent.replace(/^Bearer /, "") : null);
    }
    return Promise.reject(error);
  });
}

/**
 * The message a server fault body carries, or null when it carries none. Faults arrive as either
 * our own `{ error }` envelope or an RFC-7807 problem (`detail` / `title`). Returning null rather
 * than a fallback of its own is what lets each caller keep the fallback it has: the axios error's
 * message here, the status line in `SseHttpError` (src/sse.ts), which reads a body the same way.
 */
export function bodyMessage(data: unknown): string | null {
  const body = data as { error?: string; detail?: string; title?: string } | null | undefined;
  return body?.error ?? body?.detail ?? body?.title ?? null;
}

/**
 * Reads a human-readable message out of any failure: the fault body when there is one, otherwise
 * the thrown error's own message.
 */
export function apiErrorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    return bodyMessage(err.response?.data) ?? err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}
