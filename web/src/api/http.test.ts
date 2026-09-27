import { AxiosError, type AxiosAdapter, type InternalAxiosRequestConfig } from "axios";
import { afterEach, describe, expect, it } from "vitest";
import { clearSession, getToken, setSession } from "../auth/session";
import { agentHttp, http } from "./http";

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
