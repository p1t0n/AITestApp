import axios from "axios";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import type {
  Category,
  CategoryNode,
  Cv,
  EmployeeDetail,
  EmployeeSkill,
  EmployeeSummary,
  SaveEmployee,
  SkillDto,
} from "./types";

export const http = axios.create({ baseURL: "/api" });

// ---- Employees ----

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

// ---- Employee skills ----

export function useAddEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { skillId: string; level: string; yearsExperience: number }) =>
      (await http.post<EmployeeSkill>(`/employees/${employeeId}/skills`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (employeeSkillId: string) =>
      http.delete(`/employee-skills/${employeeSkillId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

// ---- Availability ----

export function useAddAvailability(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { effectiveFrom: string; capacityPercent: number }) =>
      (await http.post(`/employees/${employeeId}/availability`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteAvailability(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (entryId: string) => http.delete(`/availability/${entryId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

// ---- Catalog ----

export function useCategories() {
  return useQuery({
    queryKey: ["categories"],
    queryFn: async () => (await http.get<Category[]>("/catalog/categories")).data,
  });
}

export function useCategoryTree() {
  return useQuery({
    queryKey: ["categories", "tree"],
    queryFn: async () => (await http.get<CategoryNode[]>("/catalog/categories/tree")).data,
  });
}

export function useSkills() {
  return useQuery({
    queryKey: ["skills"],
    queryFn: async () => (await http.get<SkillDto[]>("/catalog/skills")).data,
  });
}

export function useCreateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { name: string; parentId: string | null }) =>
      (await http.post<Category>("/catalog/categories", dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["categories"] }),
  });
}

export function useCreateSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { name: string; categoryId: string }) =>
      (await http.post<SkillDto>("/catalog/skills", dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function apiErrorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data as { detail?: string; title?: string } | undefined;
    return data?.detail ?? data?.title ?? err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}
