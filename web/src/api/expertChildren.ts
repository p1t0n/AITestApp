// Every child collection hanging off one expert: skills, availability, languages,
// qualifications, experiences (P1T-142).
//
// These are only ever read back through that expert's detail projection, so every mutation
// invalidates ["experts", expertId] and nothing else — with one exception: the three
// experience mutations also invalidate ["experts", expertId, "cv"], because an experience edit
// changes the rendered CV. That key is invalidated from nowhere else.
//
// Availability is a step function over time (EffectiveFrom + CapacityPercent), not a flag. It and
// expert skills gained their update hooks in P1T-156, which gave both the same add/edit dialog
// the other three children already had.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type {
  AvailabilityEntry,
  ExpertSkill,
  Experience,
  Qualification,
  SaveAvailabilityEntry,
  SaveExpertSkill,
  SaveExperience,
  SaveQualification,
  SaveSpokenLanguage,
  SpokenLanguage,
} from "../types";
import { http } from "./http";

export function useAddExpertSkill(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveExpertSkill) =>
      (await http.post<ExpertSkill>(`/experts/${expertId}/skills`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

/**
 * The level and the years, never the catalog link (P1T-156): `ExpertSkillService.UpdateAsync`
 * validates `skillId` and then assigns only `Level` and `YearsExperience`. The id still rides along
 * so the payload is the one shape the API documents, and the form does not offer to change it.
 */
export function useUpdateExpertSkill(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveExpertSkill & { id: string }) =>
      (await http.put<ExpertSkill>(`/expert-skills/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useDeleteExpertSkill(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (expertSkillId: string) =>
      http.delete(`/expert-skills/${expertSkillId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

// ---- Availability ----

export function useAddAvailability(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveAvailabilityEntry) =>
      (await http.post<AvailabilityEntry>(`/experts/${expertId}/availability`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useUpdateAvailability(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveAvailabilityEntry & { id: string }) =>
      (await http.put<AvailabilityEntry>(`/availability/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useDeleteAvailability(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (entryId: string) => http.delete(`/availability/${entryId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

// ---- Languages, qualifications, experiences (P1T-142) ----

export function useAddLanguage(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveSpokenLanguage) =>
      (await http.post<SpokenLanguage>(`/experts/${expertId}/languages`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useUpdateLanguage(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveSpokenLanguage & { id: string }) =>
      (await http.put<SpokenLanguage>(`/languages/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useDeleteLanguage(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/languages/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useAddQualification(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveQualification) =>
      (await http.post<Qualification>(`/experts/${expertId}/qualifications`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useUpdateQualification(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveQualification & { id: string }) =>
      (await http.put<Qualification>(`/qualifications/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useDeleteQualification(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/qualifications/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["experts", expertId] }),
  });
}

export function useAddExperience(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveExperience) =>
      (await http.post<Experience>(`/experts/${expertId}/experiences`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["experts", expertId] });
      qc.invalidateQueries({ queryKey: ["experts", expertId, "cv"] });
    },
  });
}

export function useUpdateExperience(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveExperience & { id: string }) =>
      (await http.put<Experience>(`/experiences/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["experts", expertId] });
      qc.invalidateQueries({ queryKey: ["experts", expertId, "cv"] });
    },
  });
}

export function useDeleteExperience(expertId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/experiences/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["experts", expertId] });
      qc.invalidateQueries({ queryKey: ["experts", expertId, "cv"] });
    },
  });
}
