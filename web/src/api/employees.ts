// The employee aggregate root: the roster list, one employee's detail projection, the assembled CV,
// and the publication gate. Child collections live in ./employeeChildren — they invalidate the same
// keys but they are a different kind of write.
//
// Query keys, invalidated by prefix:
//   ["employees"]              the list
//   ["employees", id]          one detail projection
//   ["employees", id, "cv"]    the assembled CV
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { Cv, EmployeeDetail, EmployeeSummary, SaveEmployee } from "../types";
import { http } from "./http";

export function useEmployees() {
  return useQuery({
    queryKey: ["employees"],
    queryFn: async () => (await http.get<EmployeeSummary[]>("/employees")).data,
  });
}

export function useEmployee(id: string) {
  return useQuery({
    queryKey: ["employees", id],
    queryFn: async () => (await http.get<EmployeeDetail>(`/employees/${id}`)).data,
    enabled: !!id,
  });
}

export function useCv(id: string) {
  return useQuery({
    queryKey: ["employees", id, "cv"],
    queryFn: async () => (await http.get<Cv>(`/employees/${id}/cv`)).data,
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
      const res = await http.get<Blob>(`/employees/${id}/cv.pdf`, { responseType: "blob" });
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

export function useCreateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveEmployee) =>
      (await http.post<EmployeeDetail>("/employees", dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees"] }),
  });
}

export function useUpdateEmployee(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveEmployee) =>
      (await http.put<EmployeeDetail>(`/employees/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees"] });
      qc.invalidateQueries({ queryKey: ["employees", id] });
    },
  });
}

export function useDeleteEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/employees/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees"] }),
  });
}

/** The human publication gate: flips a Draft to Active (requires a valid email server-side). */
export function usePromoteEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (await http.post<EmployeeDetail>(`/employees/${id}/promote`)).data,
    onSuccess: (_, id) => {
      qc.invalidateQueries({ queryKey: ["employees"] });
      qc.invalidateQueries({ queryKey: ["employees", id] });
    },
  });
}
