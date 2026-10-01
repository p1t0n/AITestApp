import { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from "axios";
import { afterEach, describe, expect, it } from "vitest";
import { clearSession, getToken, setSession } from "../auth/session";
import { agentHttp, apiErrorMessage, bodyMessage, http } from "./http";

// An expired or refused token comes back as 401 from either host. Clearing the session is what
// sends the user to /signin: the router's gate reads the token reactively (App.tsx).

function respondWith(status: number, onRequest?: (config: InternalAxiosRequestConfig) => void): AxiosAdapter {
  return async (config) => {
    onRequest?.(config);
    const response = { status, statusText: "", data: "", headers: {}, config };
    if (status >= 200 && status < 300) return response;
    throw new AxiosError(`Request failed with status code ${status}`, "ERR_BAD_REQUEST", config, null, response);
  };
}

afterEach(() => {
  http.defaults.adapter = undefined;
  agentHttp.defaults.adapter = undefined;
  clearSession();
});

describe.each([
  ["http", http],
  ["agentHttp", agentHttp],
])("%s on 401", (_name, client) => {
  it("clears the session that sent the request", async () => {
    setSession("expired-token", "sm@example.com", "User", "u1");
    client.defaults.adapter = respondWith(401);

    await expect(client.get("/anything")).rejects.toThrow();

    expect(getToken()).toBeNull();
  });

  it("keeps a session signed in after the stale request left", async () => {
    setSession("old-token", "sm@example.com", "User", "u1");
    client.defaults.adapter = respondWith(401, () => setSession("new-token", "sm@example.com", "User", "u1"));

    await expect(client.get("/anything")).rejects.toThrow();

    expect(getToken()).toBe("new-token");
  });

  it("leaves the session alone on other failures", async () => {
    setSession("good-token", "sm@example.com", "User", "u1");
    client.defaults.adapter = respondWith(403);

    await expect(client.get("/anything")).rejects.toThrow();

    expect(getToken()).toBe("good-token");
  });
});

// `bodyMessage` is the one reader of a fault body in the SPA: `apiErrorMessage` uses it for axios
// failures and `sse.ts` for a pre-stream HTTP failure, so the two paths cannot drift apart on
// which field wins. Null, rather than a fallback of its own, is what lets each caller keep the
// fallback it already had (the axios error's message; "Request failed with status code N").

describe("bodyMessage", () => {
  it.each([
    ["our own envelope", { error: "Cap reached" }, "Cap reached"],
    ["an RFC-7807 detail", { detail: "Expert not found", title: "Not Found" }, "Expert not found"],
    ["an RFC-7807 title alone", { title: "Not Found" }, "Not Found"],
    ["error winning over both", { error: "Cap reached", detail: "d", title: "t" }, "Cap reached"],
  ])("reads %s", (_case, body, expected) => {
    expect(bodyMessage(body)).toBe(expected);
  });

  it.each([
    ["null", null],
    ["undefined", undefined],
    ["a non-JSON body", "<html>502</html>"],
    ["a JSON object with none of the three fields", { status: 500 }],
  ])("returns null for %s, leaving the fallback to the caller", (_case, body) => {
    expect(bodyMessage(body)).toBeNull();
  });
});

describe("apiErrorMessage", () => {
  it("prefers the fault body over the axios message", () => {
    const config = { headers: {} } as InternalAxiosRequestConfig;
    const response = { status: 429, statusText: "", headers: {}, config, data: { error: "Cap reached" } };
    const err = new AxiosError("Request failed with status code 429", "ERR_BAD_REQUEST", config, null, response);

    expect(apiErrorMessage(err)).toBe("Cap reached");
  });

  it("falls back to the axios message when the body carries none", () => {
    const config = { headers: {} } as InternalAxiosRequestConfig;
    const response = { status: 500, statusText: "", headers: {}, config, data: "" };
    const err = new AxiosError("Request failed with status code 500", "ERR_BAD_RESPONSE", config, null, response);

    expect(apiErrorMessage(err)).toBe("Request failed with status code 500");
  });
});
