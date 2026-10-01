import { bodyMessage } from "./api/http";
import { expireSession, getToken } from "./auth/session";

// Minimal SSE-over-POST client. The axios clients in api.ts buffer whole responses, so streaming
// endpoints (POST /agents/staffing) go through fetch + ReadableStream instead. Scope is exactly
// what our server emits: `event:`/`data:` fields, `:` comment keep-alives, blank-line frame
// delimiters, LF or CRLF endings. `id:`/`retry:` fields are ignored.

/** One parsed SSE frame: the event name (`message` when the frame has none) and the data payload
 * (multi-line `data:` fields joined with newlines, per the SSE spec). */
export interface SseMessage {
  event: string;
  data: string;
}

/** A pre-stream HTTP failure (400 validation, 401 auth, 429 cap): the response never became an
 * event stream. The message comes from `bodyMessage` (api/http.ts), the same reader `apiErrorMessage`
 * uses on axios failures, so callers can surface it directly; the parsed body rides along for
 * anything structured (e.g. the 429 cap payload). */
export class SseHttpError extends Error {
  constructor(
    readonly status: number,
    readonly data: unknown,
    message: string,
  ) {
    super(message);
    this.name = "SseHttpError";
  }
}

/**
 * POSTs `body` as JSON and streams the SSE response, invoking `onMessage` once per complete frame
 * in arrival order. Resolves when the server closes the stream; rejects with {@link SseHttpError}
 * on a non-2xx response and with the abort error when `signal` aborts mid-stream. The session
 * token (when present) is attached the same way the axios interceptors do.
 */
export async function postSse(
  url: string,
  body: unknown,
  onMessage: (message: SseMessage) => void,
  signal?: AbortSignal,
): Promise<void> {
  const token = getToken();
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(body),
    signal,
  });

  if (!response.ok) {
    if (response.status === 401) expireSession(token);
    let data: unknown = null;
    try {
      data = await response.json();
    } catch {
      // Not a JSON body; the generic message below covers it.
    }
    throw new SseHttpError(
      response.status,
      data,
      bodyMessage(data) ?? `Request failed with status code ${response.status}`,
    );
  }
  if (!response.body) {
    throw new Error("The response has no body to stream.");
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffered = "";
  let eventName = "";
  let dataLines: string[] = [];

  function dispatch() {
    if (eventName !== "" || dataLines.length > 0) {
      onMessage({ event: eventName || "message", data: dataLines.join("\n") });
    }
    eventName = "";
    dataLines = [];
  }

  function handleLine(line: string) {
    if (line === "") {
      dispatch(); // blank line = end of frame
      return;
    }
    if (line.startsWith(":")) {
      return; // comment / keep-alive
    }
    const colon = line.indexOf(":");
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? "" : line.slice(colon + 1);
    if (value.startsWith(" ")) value = value.slice(1);
    if (field === "event") eventName = value;
    else if (field === "data") dataLines.push(value);
  }

  function drainLines() {
    let newline: number;
    while ((newline = buffered.indexOf("\n")) !== -1) {
      const line = buffered.slice(0, newline);
      buffered = buffered.slice(newline + 1);
      handleLine(line.endsWith("\r") ? line.slice(0, -1) : line);
    }
  }

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffered += decoder.decode(value, { stream: true });
    drainLines();
  }

  // Flush any bytes the decoder held back plus a final unterminated line/frame.
  buffered += decoder.decode();
  drainLines();
  if (buffered !== "") handleLine(buffered.endsWith("\r") ? buffered.slice(0, -1) : buffered);
  dispatch();
}
