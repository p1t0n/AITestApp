// Erasure (P1T-186): a person deleting themselves, account and record together. No id travels —
// the row erased is always the caller's own, and the API has no way to name anybody else's.
import { useMutation } from "@tanstack/react-query";
import { http } from "./http";

export interface ErasureResult {
  /** The record that went, or null when the account owned none. */
  expertId: string | null;
  scoringRowsDeleted: number;
  /** Rows kept as decision records and hollowed out. */
  proposalRowsScrubbed: number;
  packagesRewritten: number;
}

/**
 * Deletes the signed-in person's account and record. Irreversible, synchronous, and gated by the
 * control word — the only proof-of-person this service has, since there is no email to send a
 * confirmation link to and no way to tell anybody afterwards that it happened.
 *
 * <p>No cache invalidation on success, deliberately: the session it was called with is already
 * dead, so the caller signs out rather than refetching anything.</p>
 */
export function useEraseMyAccount() {
  return useMutation({
    mutationFn: async (controlWord: string) =>
      (await http.post<ErasureResult>("/me/account/erase", { controlWord })).data,
  });
}
