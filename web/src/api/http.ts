// Transport for the two backends the SPA talks to. Nothing above this module knows a base URL,
// and nothing below it knows about React Query.
import axios from "axios";
import { getToken } from "../auth/session";

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
}

/**
 * Reads a human-readable message out of any failure. Server faults arrive as either our own
 * `{ error }` envelope or an RFC-7807 problem (`detail` / `title`); everything else falls back to
 * the thrown error's own message. `SseHttpError` (src/sse.ts) reads a body the same way.
 */
export function apiErrorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data as { error?: string; detail?: string; title?: string } | undefined;
    return data?.error ?? data?.detail ?? data?.title ?? err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}
