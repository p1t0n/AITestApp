// The expert aggregate root: the roster list, one expert's detail projection, the assembled CV,
// and the publication gate. Child collections live in ./expertChildren — they invalidate the same
// keys but they are a different kind of write.
//
// Query keys, invalidated by prefix:
//   ["experts"]              the list
//   ["experts", id]          one detail projection
//   ["experts", id, "cv"]    the assembled CV
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Cv, ExpertDetail, ExpertSummary, SaveExpert } from "../types";
import { http } from "./http";

export function useExperts() {
  return useQuery({
    queryKey: ["experts"],
    queryFn: async () => (await http.get<ExpertSummary[]>("/experts")).data,
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
      const res = await http.get<Blob>(`/experts/${id}/cv.pdf`, { responseType: "blob" });
      const disposition = (res.headers["content-disposition"] as string | undefined) ?? "";
      const filename = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? "cv.pdf";
      const url = URL.createObjectURL(res.data);
      try {
        const link = document.createElement("a");
        link.href = url;
        link.download = filename;
        link.click();
      } finally {
        URL.revokeObjectURL(url);
      }
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
