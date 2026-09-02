// What we hold on you, and a copy of it (P1T-187) — Art. 15 access and Art. 20 portability.
//
// Two calls rather than one, because they owe opposite things: the access view includes what
// software worked out about the person, and the portable copy excludes it.
//
// Query keys, invalidated by prefix:
//   ["me", "access"]   the Art. 15 view of the signed-in person's own record
import { useMutation, useQuery } from "@tanstack/react-query";
import { http } from "./http";
import { saveAsFile } from "./download";

export type ExportEntitlement = "Right" | "Courtesy";

export interface RecipientCategory {
  recipient: string;
  why: string;
}

/** One automated assessment of this person, as they are entitled to read it. */
export interface DerivedAssessment {
  source: string;
  sourceId: string;
  at: string | null;
  score: number | null;
  band: string | null;
  rationale: string | null;
  digest: string | null;
  matchAnswer: string | null;
}

export interface AccessView {
  expertId: string;
  origin: "SelfRegistered" | "StaffCreated";
  basis: "ContractNecessity" | "LegitimateInterest";
  /** Art. 15(1)(g), and only where the data did not come from them. */
  source: string | null;
  noticeVersionAcknowledged: string | null;
  pausedSince: string | null;
  export: ExportEntitlement;
  purposes: string[];
  dataCategories: string[];
  recipients: RecipientCategory[];
  retention: string;
  /** Markdown: the procedure and principles actually applied. */
  art22Logic: string;
  rights: string[];
  complaintRight: string;
  record: unknown;
  derived: { assessments: DerivedAssessment[]; searchIndexNote: string };
  history: unknown[];
}

/** The Art. 15 view of the signed-in person's own record. 404s for somebody who owns none. */
export function useMyAccessView(enabled = true) {
  return useQuery({
    queryKey: ["me", "access"],
    queryFn: async () => (await http.get<AccessView>("/me/access")).data,
    enabled,
    retry: false,
  });
}

/** Downloads the caller's own portable copy. Writes no record: reading your own data is not an
 * event worth logging, and logging it would be the read log this design refused. */
export function useDownloadMyExport() {
  return useMutation({
    mutationFn: async () => {
      const response = await http.get<Blob>("/me/export", { responseType: "blob" });
      saveAsFile(response, "experttojob-export.json");
    },
  });
}

/**
 * A Service Manager taking somebody's copy for them — the phoned-in request, since this service has
 * no email to receive one by. A POST because it is not a plain read: it writes a record of the
 * staff member who did it.
 */
export function useExportExpertOnBehalf(expertId: string) {
  return useMutation({
    mutationFn: async () => {
      const response = await http.post<Blob>(
        `/experts/${expertId}/export`, {}, { responseType: "blob" });
      saveAsFile(response, `experttojob-export-${expertId}.json`);
    },
  });
}
