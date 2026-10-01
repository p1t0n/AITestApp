// Every child collection hanging off one expert: skills, availability, languages,
// qualifications, experiences (P1T-142).
//
// All five collections are the same three mutations over two URLs — a collection URL under the
// expert to add to, and a top-level item URL to update or delete — so they are generated from one
// factory rather than written out fifteen times (EXP-80). The exported names are unchanged, and
// each is the only thing a component ever imports.
//
// They are only ever read back through that expert's detail projection, so every mutation
// invalidates ["experts", expertId] and nothing else. That covers the CV too: React Query matches
// query keys by prefix, so ["experts", expertId, "cv"] is already in scope — the same reason given
// at ./agents/tailoring.ts. expertChildren.test.tsx watches the CV query itself, so a change to
// the key shape that broke that relationship would fail there rather than leave a stale CV up.
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

/**
 * The add/update/delete trio for one child collection.
 *
 * `collectionPath` is the segment under the expert that POST adds to; `itemPath` is the top-level
 * segment that addresses one row for PUT and DELETE. They differ per collection (skills post to
 * `/experts/:id/skills` but update at `/expert-skills/:itemId`), which is why both are arguments
 * and neither is derived from the other.
 */
function childCrud<Save extends object, Row>(collectionPath: string, itemPath: string) {
  /** Refresh the expert's detail projection — and, by prefix, everything hanging off it. */
  function useRefreshExpert(expertId: string) {
    const qc = useQueryClient();
    return () => qc.invalidateQueries({ queryKey: ["experts", expertId] });
  }

  function useAdd(expertId: string) {
    const onSuccess = useRefreshExpert(expertId);
    return useMutation({
      mutationFn: async (dto: Save) =>
        (await http.post<Row>(`/experts/${expertId}/${collectionPath}`, dto)).data,
      onSuccess,
    });
  }

  function useUpdate(expertId: string) {
    const onSuccess = useRefreshExpert(expertId);
    return useMutation({
      // The id addresses the row; it is not part of the payload.
      mutationFn: async ({ id, ...dto }: Save & { id: string }) =>
        (await http.put<Row>(`/${itemPath}/${id}`, dto)).data,
      onSuccess,
    });
  }

  function useDelete(expertId: string) {
    const onSuccess = useRefreshExpert(expertId);
    return useMutation({
      mutationFn: async (id: string) => http.delete(`/${itemPath}/${id}`),
      onSuccess,
    });
  }

  return { useAdd, useUpdate, useDelete };
}

const expertSkills = childCrud<SaveExpertSkill, ExpertSkill>("skills", "expert-skills");
const availability = childCrud<SaveAvailabilityEntry, AvailabilityEntry>("availability", "availability");
const languages = childCrud<SaveSpokenLanguage, SpokenLanguage>("languages", "languages");
const qualifications = childCrud<SaveQualification, Qualification>("qualifications", "qualifications");
const experiences = childCrud<SaveExperience, Experience>("experiences", "experiences");

export const useAddExpertSkill = expertSkills.useAdd;
/**
 * The level and the years, never the catalog link (P1T-156): `ExpertSkillService.UpdateAsync`
 * validates `skillId` and then assigns only `Level` and `YearsExperience`. The id still rides along
 * so the payload is the one shape the API documents, and the form does not offer to change it.
 */
export const useUpdateExpertSkill = expertSkills.useUpdate;
export const useDeleteExpertSkill = expertSkills.useDelete;

// ---- Availability ----

export const useAddAvailability = availability.useAdd;
export const useUpdateAvailability = availability.useUpdate;
export const useDeleteAvailability = availability.useDelete;

// ---- Languages, qualifications, experiences (P1T-142) ----

export const useAddLanguage = languages.useAdd;
export const useUpdateLanguage = languages.useUpdate;
export const useDeleteLanguage = languages.useDelete;

export const useAddQualification = qualifications.useAdd;
export const useUpdateQualification = qualifications.useUpdate;
export const useDeleteQualification = qualifications.useDelete;

export const useAddExperience = experiences.useAdd;
export const useUpdateExperience = experiences.useUpdate;
export const useDeleteExperience = experiences.useDelete;
