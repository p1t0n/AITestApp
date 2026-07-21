import { afterEach, describe, expect, it, vi } from "vitest";
import { postSse, SseHttpError, type SseMessage } from "./sse";

// ---- synthetic stream plumbing ----
// postSse is fetch-based; these tests feed it hand-built ReadableStreams so every framing rule
// (event/data lines, comments, multi-line data, chunk boundaries, abort) is exercised without a
// server.

function streamOf(chunks: string[], signal?: AbortSignal): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      // Mirror real fetch behaviour: aborting the request errors the body stream.
      signal?.addEventListener("abort", () =>
        controller.error(new DOMException("The operation was aborted.", "AbortError")),
      );
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      if (!signal) controller.close();
    },
  });
}

function okResponse(body: ReadableStream<Uint8Array>) {
  return { ok: true, status: 200, body, json: async () => ({}) };
}

function mockFetch(response: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(response);
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

async function collect(chunks: string[]): Promise<SseMessage[]> {
  mockFetch(okResponse(streamOf(chunks)));
  const messages: SseMessage[] = [];
  await postSse("/agents/staffing", { jobDescription: "x" }, (m) => messages.push(m));
  return messages;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("postSse", () => {
  it("POSTs the body as JSON and asks for an event stream", async () => {
    const fetchMock = mockFetch(okResponse(streamOf(["event: step\ndata: {}\n\n"])));
    await postSse("/agents/staffing", { jobDescription: "jd", matchTop: 3 }, () => {});

    expect(fetchMock).toHaveBeenCalledWith(
      "/agents/staffing",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ jobDescription: "jd", matchTop: 3 }),
        headers: expect.objectContaining({
          "Content-Type": "application/json",
          Accept: "text/event-stream",
        }),
      }),
    );
  });

  it("parses event/data frames and dispatches them in order", async () => {
    const messages = await collect([
      'event: step\ndata: {"stage":"shortlist","status":"started"}\n\n',
      'event: report\ndata: {"degraded":false}\n\n',
    ]);

    expect(messages).toEqual([
      { event: "step", data: '{"stage":"shortlist","status":"started"}' },
      { event: "report", data: '{"degraded":false}' },
    ]);
  });

  it("ignores comment (keep-alive) lines", async () => {
    const messages = await collect([
      ": ka\n\n",
      ": ka\nevent: step\ndata: {}\n\n",
      ": ka\n\n",
    ]);

    expect(messages).toEqual([{ event: "step", data: "{}" }]);
  });

  it("joins multi-line data with newlines", async () => {
    const messages = await collect(["event: report\ndata: line one\ndata: line two\n\n"]);

    expect(messages).toEqual([{ event: "report", data: "line one\nline two" }]);
  });

  it("defaults the event name to 'message' when the frame has no event line", async () => {
    const messages = await collect(["data: hello\n\n"]);

    expect(messages).toEqual([{ event: "message", data: "hello" }]);
  });

  it("reassembles frames split across arbitrary chunk boundaries", async () => {
    const messages = await collect([
      "eve",
      "nt: st",
      'ep\ndata: {"a"',
      ':1}\n\nevent: repo',
      "rt\ndata: {}\n",
      "\n",
    ]);

    expect(messages).toEqual([
      { event: "step", data: '{"a":1}' },
      { event: "report", data: "{}" },
    ]);
  });

  it("handles CRLF line endings", async () => {
    const messages = await collect(["event: step\r\ndata: {}\r\n\r\n"]);

    expect(messages).toEqual([{ event: "step", data: "{}" }]);
  });

  it("throws an SseHttpError carrying the parsed body on a pre-stream HTTP failure", async () => {
    mockFetch({
      ok: false,
      status: 429,
      json: async () => ({
        error: "Your daily token cap has been reached.",
        window: "daily",
        used: 1000,
        cap: 1000,
      }),
    });

    const failure = await postSse("/agents/staffing", {}, () => {}).catch((e: unknown) => e);

    expect(failure).toBeInstanceOf(SseHttpError);
    const httpError = failure as SseHttpError;
    expect(httpError.status).toBe(429);
    expect(httpError.message).toBe("Your daily token cap has been reached.");
    expect(httpError.data).toMatchObject({ window: "daily", used: 1000, cap: 1000 });
  });

  it("falls back to a generic message when the failure body is not JSON", async () => {
    mockFetch({
      ok: false,
      status: 502,
      json: async () => {
        throw new Error("not json");
      },
    });

    const failure = await postSse("/agents/staffing", {}, () => {}).catch((e: unknown) => e);

    expect(failure).toBeInstanceOf(SseHttpError);
    expect((failure as SseHttpError).message).toMatch(/502/);
  });

  it("rejects with the abort error when the signal aborts mid-stream", async () => {
    const controller = new AbortController();
    mockFetch(okResponse(streamOf(["event: step\ndata: {}\n\n"], controller.signal)));
    const messages: SseMessage[] = [];

    const run = postSse("/agents/staffing", {}, (m) => {
      messages.push(m);
      controller.abort();
    }, controller.signal);

    await expect(run).rejects.toMatchObject({ name: "AbortError" });
    expect(messages).toEqual([{ event: "step", data: "{}" }]);
  });
});
