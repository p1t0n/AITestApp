import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it } from "vitest";
import type { AxiosAdapter, InternalAxiosRequestConfig } from "axios";
import axios from "axios";
import { ROSTER_DEFAULTS, useRosterPage, type RosterQuery } from "./experts";
import { http } from "./http";

/**
 * How the roster's filters reach the wire (EXP-47).
 *
 * This is one line of configuration and it is invisible in every other test, which is exactly why
 * it needs its own: axios indexes array params as `statuses[0]=Active` by default, ASP.NET Core
 * binds neither that nor the `statuses[]=` form to a `string[]` action parameter, and the failure
 * is silent — the request succeeds, the filter is simply absent, and the roster comes back
 * unnarrowed. Nothing in the component tests would notice, because they stop at the query object.
 */

let sent: InternalAxiosRequestConfig | null = null;

const capture: AxiosAdapter = async (config) => {
  sent = config;
  return {
    status: 200,
    statusText: "OK",
    data: { items: [], total: 0, facets: { status: [], band: [], location: [] } },
    headers: {},
    config,
  };
};

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

/** The query string the roster hook actually put on the wire. */
async function askedFor(query: RosterQuery): Promise<string> {
  http.defaults.adapter = capture;
  const { result } = renderHook(() => useRosterPage(query), { wrapper });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  return axios.getUri({ url: sent!.url, params: sent!.params, paramsSerializer: sent!.paramsSerializer });
}

afterEach(() => {
  http.defaults.adapter = undefined;
  sent = null;
});

describe("the roster request", () => {
  it("repeats a key per value instead of indexing it, which is the form the server binds", async () => {
    const uri = await askedFor({
      ...ROSTER_DEFAULTS,
      statuses: ["Active", "Draft"],
      locations: ["London", "Arlington"],
    });

    expect(uri).toContain("statuses=Active&statuses=Draft");
    expect(uri).toContain("locations=London&locations=Arlington");
    expect(uri).not.toContain("%5B"); // no `[`: neither statuses[0]= nor statuses[]=
  });

  it("leaves a location's comma alone rather than splitting it into two places", async () => {
    const uri = await askedFor({ ...ROSTER_DEFAULTS, locations: ["Cambridge, MA"] });

    // One key, comma and all — the reason the wire format is repeated keys rather than one
    // comma-joined value. (The space rides as `+`, which is a query string's spelling of a space.)
    expect(uri.match(/locations=/g)).toHaveLength(1);
    expect(uri).toContain("locations=Cambridge,+MA");
  });

  it("omits the band entirely for Any, rather than sending the string 'null'", async () => {
    const uri = await askedFor({ ...ROSTER_DEFAULTS, band: null });

    expect(uri).not.toContain("band");
  });

  it("sends the band when one is chosen", async () => {
    const uri = await askedFor({ ...ROSTER_DEFAULTS, band: "full" });

    expect(uri).toContain("band=full");
  });

  it("sends an unfiltered view with no filter keys at all", async () => {
    const uri = await askedFor(ROSTER_DEFAULTS);

    expect(uri).not.toContain("statuses");
    expect(uri).not.toContain("locations");
  });
});
