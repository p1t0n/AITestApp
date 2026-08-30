// Every child collection hanging off one employee: skills, availability, languages,
// qualifications, experiences (P1T-142).
//
// These are only ever read back through that employee's detail projection, so every mutation
// invalidates ["employees", employeeId] and nothing else — with one exception: the three
// experience mutations also invalidate ["employees", employeeId, "cv"], because an experience edit
// changes the rendered CV. That key is invalidated from nowhere else.
//
// Availability is a step function over time (EffectiveFrom + CapacityPercent), not a flag. It and
// employee skills gained their update hooks in P1T-156, which gave both the same add/edit dialog
// the other three children already had.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type {
  AvailabilityEntry,
  EmployeeSkill,
  Experience,
  Qualification,
  SaveAvailabilityEntry,
  SaveEmployeeSkill,
  SaveExperience,
  SaveQualification,
  SaveSpokenLanguage,
  SpokenLanguage,
} from "../types";
import { http } from "./http";

export function useAddEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveEmployeeSkill) =>
      (await http.post<EmployeeSkill>(`/employees/${employeeId}/skills`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

/**
 * The level and the years, never the catalog link (P1T-156): `EmployeeSkillService.UpdateAsync`
 * validates `skillId` and then assigns only `Level` and `YearsExperience`. The id still rides along
 * so the payload is the one shape the API documents, and the form does not offer to change it.
 */
export function useUpdateEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveEmployeeSkill & { id: string }) =>
      (await http.put<EmployeeSkill>(`/employee-skills/${id}`, dto)).data,
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
    mutationFn: async (dto: SaveAvailabilityEntry) =>
      (await http.post<AvailabilityEntry>(`/employees/${employeeId}/availability`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useUpdateAvailability(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveAvailabilityEntry & { id: string }) =>
      (await http.put<AvailabilityEntry>(`/availability/${id}`, dto)).data,
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

// ---- Languages, qualifications, experiences (P1T-142) ----

export function useAddLanguage(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveSpokenLanguage) =>
      (await http.post<SpokenLanguage>(`/employees/${employeeId}/languages`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useUpdateLanguage(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveSpokenLanguage & { id: string }) =>
      (await http.put<SpokenLanguage>(`/languages/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteLanguage(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/languages/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useAddQualification(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveQualification) =>
      (await http.post<Qualification>(`/employees/${employeeId}/qualifications`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useUpdateQualification(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveQualification & { id: string }) =>
      (await http.put<Qualification>(`/qualifications/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteQualification(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/qualifications/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useAddExperience(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveExperience) =>
      (await http.post<Experience>(`/employees/${employeeId}/experiences`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees", employeeId] });
      qc.invalidateQueries({ queryKey: ["employees", employeeId, "cv"] });
    },
  });
}

export function useUpdateExperience(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveExperience & { id: string }) =>
      (await http.put<Experience>(`/experiences/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees", employeeId] });
      qc.invalidateQueries({ queryKey: ["employees", employeeId, "cv"] });
    },
  });
}

export function useDeleteExperience(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/experiences/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees", employeeId] });
      qc.invalidateQueries({ queryKey: ["employees", employeeId, "cv"] });
    },
  });
}
