// The expert aggregate root: the roster list, one expert's detail projection, the assembled CV,
// and the publication gate. Child collections live in ./expertChildren — they invalidate the same
// keys but they are a different kind of write.
//
// Query keys, invalidated by prefix:
//   ["experts"]              the bench list (Active only)
//   ["experts", "roster"]    the whole Roster, Drafts included — the staff list
//   ["experts", id]          one detail projection
//   ["experts", id, "cv"]    the assembled CV
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
