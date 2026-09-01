// Resume ingestion (P1T-96). The only agent that writes to the roster, so it invalidates
// ["experts"] as well as the ledger; what it creates lands as a Draft that a human promotes
// (usePromoteExpert, in ../experts).
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface IngestionCreated {
  languages: number;
  skills: number;
  qualifications: number;
  experiences: number;
}

/** The composed ingestion result: deterministic fields from captured tool results; proposals are
 * catalog-unmatched skill names awaiting a human decision. */
export interface IngestionResponse {
  expertId: string;
  created: IngestionCreated;
  proposals: string[];
  notes: string[];
  duplicateWarning: string | null;
  degraded: boolean;
}

export function useResumeIngestion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (resumeText: string) =>
      (await agentHttp.post<IngestionResponse>("/resume-ingestion", { resumeText })).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["usage"] });
      qc.invalidateQueries({ queryKey: ["experts"] });
    },
  });
}

