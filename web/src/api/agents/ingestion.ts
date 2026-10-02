// Resume ingestion (P1T-96). The only agent that writes to the roster, so it invalidates
// ["experts"] as well as the ledger; what it creates lands as a Draft that a human promotes
// (usePromoteExpert, in ../experts).
import { agentPost } from "./shared";

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

export const useResumeIngestion = agentPost<{ resumeText: string }, IngestionResponse>(
  "/resume-ingestion",
  { onSuccess: (qc) => qc.invalidateQueries({ queryKey: ["experts"] }) },
);

