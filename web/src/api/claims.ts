// Claims: how a person comes to own a roster row (P1T-184). Email is never verified here and the
// service sends no mail, so a matching address raises a request a Service Manager decides — and the
// one thing stronger than a match is a single-use code handed over out of band.
//
// Query keys, invalidated by prefix:
//   ["claims"]                     the Service Manager's queue: open claims and raised flags
//   ["claims", "ownership", id]    who owns one roster row
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { http } from "./http";

export type ClaimState = "Pending" | "Approved" | "Rejected" | "Ambiguous";

export interface ClaimQueueItem {
  id: string;
  claimantUserId: string;
  /** The address that matched, as it stood when the claim was raised. */
  claimantEmail: string;
  /** Null on a raised flag: no row was picked, because no row could be. */
  expertId: string | null;
  expertName: string | null;
  expertEmail: string | null;
  /** How many non-draft rows the address matched. 1 for a claim, 0 or 2+ for a flag. */
  matchCount: number;
  state: ClaimState;
  createdAt: string;
}

export interface ClaimCodeIssued {
  expertId: string;
  /** The only copy that will ever exist — the server stores a hash. */
  code: string;
}

export interface ExpertOwnership {
  expertId: string;
  /** The account this row belongs to, or null when nobody has claimed it. */
  ownerUserId: string | null;
  ownerEmail: string | null;
}

/**
 * Who owns one roster row. A read of its own rather than two fields on the expert projection:
 * that projection is what the MCP tools hand every agent on every model call, and ownership is a
 * staff concern no agent acts on.
 */
export function useExpertOwnership(expertId: string) {
  return useQuery({
    queryKey: ["claims", "ownership", expertId],
    queryFn: async () =>
      (await http.get<ExpertOwnership>(`/claims/ownership/${expertId}`)).data,
    enabled: !!expertId,
  });
}

/** Open claims and raised flags, oldest first. Service Manager only. */
export function useClaimQueue() {
  return useQuery({
    queryKey: ["claims"],
    queryFn: async () => (await http.get<ClaimQueueItem[]>("/claims")).data,
  });
}

/** Binds the row to the claimant and moves its lawful basis to Art. 6(1)(b). */
export function useApproveClaim() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (await http.post<ClaimQueueItem>(`/claims/${id}/approve`, {})).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["claims"] });
      qc.invalidateQueries({ queryKey: ["experts"] });
    },
  });
}

/** Refuses a claim, or dismisses a raised flag. The record is kept either way. */
export function useRejectClaim() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (await http.post<ClaimQueueItem>(`/claims/${id}/reject`, {})).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["claims"] }),
  });
}

/** Issues a single-use code for a row. Show the plaintext once; it is not recoverable. */
export function useIssueClaimCode() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (expertId: string) =>
      (await http.post<ClaimCodeIssued>("/claims/codes", { expertId })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["claims"] }),
  });
}

/** Spends a code and binds ownership — no approval step, because the code is the proof. */
export function useRedeemClaimCode() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (code: string) =>
      (await http.post<{ expertId: string }>("/claims/redeem", { code })).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["claims"] });
      qc.invalidateQueries({ queryKey: ["experts"] });
    },
  });
}

/**
 * Unbinds a row. The consequence chains and the calling screen has to say so: revoked means
 * unowned, which means legitimate interest, which means the row is no longer scanned.
 */
export function useRevokeOwnership() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (expertId: string) => http.post("/claims/revoke", { expertId }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["claims"] });
      qc.invalidateQueries({ queryKey: ["experts"] });
    },
  });
}
