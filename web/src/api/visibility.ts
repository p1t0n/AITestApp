// The pause control (P1T-185): an Expert taking themselves off the bench, and putting themselves
// back on it. Every call is about the caller's own record — there is no expert id anywhere in this
// module, because hiding is the Expert's own act and the API cannot express doing it to somebody
// else.
//
// Query keys, invalidated by prefix:
//   ["me", "visibility"]   whether the signed-in person's own record is paused
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { http } from "./http";

export interface ExpertVisibility {
  expertId: string;
  hidden: boolean;
  /** When the pause started — what "since when" is answered from. Null while on the bench. */
  hiddenSince: string | null;
}

/**
 * Where the caller's own record stands. 404s for somebody who owns no record at all, which is the
 * same answer every own-row read gives them (P1T-182) — so this query is `enabled` only where a
 * record is expected, and its absence is not an error worth shouting about.
 */
export function useMyVisibility(enabled = true) {
  return useQuery({
    queryKey: ["me", "visibility"],
    queryFn: async () => (await http.get<ExpertVisibility>("/me/visibility")).data,
    enabled,
    retry: false,
  });
}

/** Pauses or resumes the caller's own record. */
export function useSetMyVisibility() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (hidden: boolean) =>
      (await http.post<ExpertVisibility>(`/me/visibility/${hidden ? "hide" : "unhide"}`, {})).data,
    onSuccess: (visibility) => {
      qc.setQueryData(["me", "visibility"], visibility);
      qc.invalidateQueries({ queryKey: ["experts"] });
    },
  });
}
