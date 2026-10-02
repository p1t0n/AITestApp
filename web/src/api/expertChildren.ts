// Every child collection hanging off one expert: skills, availability, languages,
// qualifications, experiences (P1T-142).
//
// All five collections are the same two mutations over two URLs — a collection URL under the
// expert to post to, and a top-level item URL to put to or delete — so they are generated from one
// factory rather than written out fifteen times (EXP-80). Each export is the only thing a
// component ever imports.
//
// Saving is one hook, not an add and an update (EXP-103). Every call site had the same `edit.id ?
// update : add` ternary, so the id now picks the verb inside the factory: present means PUT to the
// item, absent means POST to the collection. `expertChildren.test.tsx` pins that branch, because
// it is no longer visible at the call site.
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
  SaveAvailabilityEntry,
  SaveExpertSkill,
  SaveExperience,
  SaveQualification,
  SaveSpokenLanguage,
} from "../types";
import { http } from "./http";

/**
 * The save/delete pair for one child collection.
 *
 * `collectionPath` is the segment under the expert that POST adds to; `itemPath` is the top-level
 * segment that addresses one row for PUT and DELETE. They differ per collection (skills post to
 * `/experts/:id/skills` but update at `/expert-skills/:itemId`), which is why both are arguments
 * and neither is derived from the other.
 *
 * Nothing reads a mutation's result — every call site awaits it and throws the row away — so the
 * response is left untyped rather than carrying a `Row` parameter no one looks at.
 */
function childCrud<Save extends object>(collectionPath: string, itemPath: string) {
  /** Refresh the expert's detail projection — and, by prefix, everything hanging off it. */
  function useRefreshExpert(expertId: string) {
    const qc = useQueryClient();
    return () => qc.invalidateQueries({ queryKey: ["experts", expertId] });
  }

  function useSave(expertId: string) {
    const onSuccess = useRefreshExpert(expertId);
    return useMutation({
      // The id addresses the row; it is not part of the payload, and an absent one means create.
      mutationFn: async ({ id, ...dto }: Save & { id?: string }) =>
        id
          ? (await http.put(`/${itemPath}/${id}`, dto)).data
          : (await http.post(`/experts/${expertId}/${collectionPath}`, dto)).data,
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

  return { useSave, useDelete };
}

const expertSkills = childCrud<SaveExpertSkill>("skills", "expert-skills");
const availability = childCrud<SaveAvailabilityEntry>("availability", "availability");
const languages = childCrud<SaveSpokenLanguage>("languages", "languages");
const qualifications = childCrud<SaveQualification>("qualifications", "qualifications");
const experiences = childCrud<SaveExperience>("experiences", "experiences");

/**
 * With an id: the level and the years, never the catalog link (P1T-156).
 * `ExpertSkillService.UpdateAsync` validates `skillId` and then assigns only `Level` and
 * `YearsExperience`. The id still rides along so the payload is the one shape the API documents,
 * and the form does not offer to change it.
 */
export const useSaveExpertSkill = expertSkills.useSave;
export const useDeleteExpertSkill = expertSkills.useDelete;

// ---- Availability ----

export const useSaveAvailability = availability.useSave;
export const useDeleteAvailability = availability.useDelete;

// ---- Languages, qualifications, experiences (P1T-142) ----

export const useSaveLanguage = languages.useSave;
export const useDeleteLanguage = languages.useDelete;

export const useSaveQualification = qualifications.useSave;
export const useDeleteQualification = qualifications.useDelete;

export const useSaveExperience = experiences.useSave;
export const useDeleteExperience = experiences.useDelete;
