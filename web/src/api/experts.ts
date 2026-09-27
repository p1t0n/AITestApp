// The expert aggregate root: the roster list, one expert's detail projection, the assembled CV,
// and the publication gate. Child collections live in ./expertChildren — they invalidate the same
// keys but they are a different kind of write.
//
// Query keys, invalidated by prefix:
//   ["experts"]                     the bench list (Active only)
//   ["experts", "roster"]           the whole Roster, Drafts included — the ⌘K palette's list
//   ["experts", "roster", query]    one page of it, searched and sorted by the server
//   ["experts", id]                 one detail projection
//   ["experts", id, "cv"]           the assembled CV
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Cv, ExpertDetail, ExpertSummary, SaveExpert } from "../types";
import { http } from "./http";
import { saveAsFile } from "./download";

/**
 * The **bench** list: published (Active) Experts only, which is what the server returns when
 * `includeDrafts` is not asked for. This is the list the agent widget's expert pickers offer —
 * a Draft is agent-staged and unvetted, so it is not somebody to tailor a CV for or to match
 * against a job description (EXP-49).
 */
export function useExperts() {
  return useQuery({
    queryKey: ["experts"],
    queryFn: async () => (await http.get<ExpertSummary[]>("/experts")).data,
  });
}

/**
 * The whole **Roster**: Draft, Active and Paused. The staff roster surfaces read this one —
 * the roster table and the ⌘K palette's People group — because they are where a human accounts
 * for every Expert the instance holds, including a Draft waiting at the publication gate.
 *
 * A separate request rather than a widened `useExperts`, deliberately: the pickers share that
 * hook and must keep offering only published people. It stays under the `["experts"]` prefix so
 * every existing `invalidateQueries({ queryKey: ["experts"] })` still refreshes it.
 */
export function useRoster() {
  return useQuery({
    queryKey: ["experts", "roster"],
    queryFn: async () =>
      (await http.get<ExpertSummary[]>("/experts", { params: { includeDrafts: true } })).data,
  });
}

/** The sort keys `/experts/roster` accepts. A value outside this set is a 400, by design. */
export const ROSTER_SORTS = ["name", "title", "location", "capacity", "status"] as const;
export type RosterSort = (typeof ROSTER_SORTS)[number];
export type RosterDir = "asc" | "desc";

/** The statuses the sidebar filters on, in the order it lists them. A value outside this set is a
 *  400 — Paused is not one of them, because a pause is a timestamp on an Active row. */
export const ROSTER_STATUSES = ["Active", "Draft"] as const;
export type RosterStatusFilter = (typeof ROSTER_STATUSES)[number];

/** The availability-today bands, most available first. `null` is "Any". */
export const ROSTER_BANDS = ["full", "partial", "none"] as const;
export type RosterBand = (typeof ROSTER_BANDS)[number];

/** What the roster page is currently showing — the whole of it, and the whole of the URL. */
export interface RosterQuery {
  /** Case-insensitive contains over full name, email and title. */
  q: string;
  /** Empty means every status, which is what an untouched group of checkboxes asks for. */
  statuses: RosterStatusFilter[];
  /** Exact location matches, unioned. Empty means everywhere. */
  locations: string[];
  /** null means any availability. */
  band: RosterBand | null;
  sort: RosterSort;
  dir: RosterDir;
  /** 1-based, as the server counts. */
  page: number;
  pageSize: number;
}

/** One choice in the sidebar, and how many rows it would leave. */
export interface RosterFacetCount {
  value: string;
  count: number;
}

/**
 * The counts beside the sidebar's filters (EXP-47). Each group is counted by the server against
 * every *other* active filter and never against its own, so an unchecked box says what checking it
 * would add rather than the 0 its own filter would force it to.
 */
export interface RosterFacets {
  status: RosterFacetCount[];
  band: RosterFacetCount[];
  /** Busiest first. A zero here is a row to disable, not a row to drop. */
  location: RosterFacetCount[];
}

export interface RosterPageResult {
  items: ExpertSummary[];
  /** The size of the whole match, not of this page. */
  total: number;
  facets: RosterFacets;
}

export const ROSTER_DEFAULTS: RosterQuery = {
  q: "",
  statuses: [],
  locations: [],
  band: null,
  sort: "name",
  dir: "asc",
  page: 1,
  pageSize: 25,
};

/**
 * One page of the Roster, searched, sorted and counted by the server (EXP-45).
 *
 * `keepPreviousData` is what makes paging and sorting feel like a table rather than a reload: the
 * rows already on screen stay put while the next page is in flight, so the header does not fall
 * back to a spinner and the page does not jump. It also means `isLoading` is true exactly once —
 * on the first fetch — which is what the roster's early return depends on, and what the e2e
 * capture's wait on **New expert** depends on in turn (`manuals/spa-design-system.md` §10).
 *
 * Under the `["experts"]` prefix, so the existing `invalidateQueries({ queryKey: ["experts"] })`
 * after a create or a delete still refreshes whatever page is on screen.
 */
export function useRosterPage(query: RosterQuery) {
  return useQuery({
    queryKey: ["experts", "roster", query],
    queryFn: async () =>
      (
        await http.get<RosterPageResult>("/experts/roster", {
          // `band: null` would serialise as the literal string "null" and be refused; undefined is
          // simply absent, which is what "Any" means.
          params: { ...query, band: query.band ?? undefined },
          // Repeated keys without brackets — `statuses=Active&statuses=Draft`. axios indexes array
          // params as `statuses[0]=` by default, and ASP.NET Core binds neither that nor the
          // `statuses[]=` form to a `string[]` action parameter, so both arrive as no filter at all.
          paramsSerializer: { indexes: null },
        })
      ).data,
    placeholderData: keepPreviousData,
  });
}

export function useExpert(id: string) {
  return useQuery({
    queryKey: ["experts", id],
    queryFn: async () => (await http.get<ExpertDetail>(`/experts/${id}`)).data,
    enabled: !!id,
  });
}

export function useCv(id: string) {
  return useQuery({
    queryKey: ["experts", id, "cv"],
    queryFn: async () => (await http.get<Cv>(`/experts/${id}/cv`)).data,
    enabled: !!id,
  });
}

/**
 * Server-side CV render (P1T-139). Fetched through axios rather than linked to directly so the
 * session token rides along on the request; the response is then handed to the browser as a
 * download under the filename the server chose.
 */
export function useDownloadCvPdf(id: string) {
  return useMutation({
    mutationFn: async () => {
      saveAsFile(await http.get<Blob>(`/experts/${id}/cv.pdf`, { responseType: "blob" }), "cv.pdf");
    },
  });
}

export function useCreateExpert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveExpert) =>
      (await http.post<ExpertDetail>("/experts", dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts"] }),
  });
}

export function useUpdateExpert(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveExpert) =>
      (await http.put<ExpertDetail>(`/experts/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["experts"] });
      qc.invalidateQueries({ queryKey: ["experts", id] });
    },
  });
}

export function useDeleteExpert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/experts/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts"] }),
  });
}

/** The human publication gate: flips a Draft to Active (requires a valid email server-side). */
export function usePromoteExpert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (await http.post<ExpertDetail>(`/experts/${id}/promote`)).data,
    onSuccess: (_, id) => {
      qc.invalidateQueries({ queryKey: ["experts"] });
      qc.invalidateQueries({ queryKey: ["experts", id] });
    },
  });
}
