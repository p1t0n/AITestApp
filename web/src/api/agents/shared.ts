// Contracts more than one agent surface speaks, and the one mutation shape they all have.
import { useMutation, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";

/**
 * Builds an agent mutation hook: POST a JSON body to the agents backend, return the parsed
 * response, invalidate the usage ledger. The ledger is not a nicety — every agent call spends
 * tokens, so a run that leaves it alone leaves a number on screen that the server disagrees with.
 *
 * The hook's argument is the wire payload, and `{}` when it takes none — which is what the server
 * parses either way. `onSuccess` is for the two hooks that invalidate more than the ledger.
 */
export function agentPost<Req, Res>(
  path: string,
  options: {
    onSuccess?: (qc: QueryClient, data: Res, req: Req) => void;
  } = {},
) {
  return function useAgentPost() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: async (req: Req) => (await agentHttp.post<Res>(path, req ?? {})).data,
      onSuccess: (data, req) => {
        qc.invalidateQueries({ queryKey: ["usage"] });
        options.onSuccess?.(qc, data, req);
      },
    });
  };
}

/** The input Match, CV Tailoring and Interview Kit all take: one expert, one job description. */
export interface AgentJobRequest {
  expertId: string;
  jobDescription: string;
}

export interface AgentAnswer {
  answer: string;
}

// ---- JD extraction (P1T-117/120) ----
// The structured reading of the JD that fed retrieval: priorities, evidence spans, inferred
// badges, ambiguities. Additive on shortlist/staffing responses; absent on degraded runs.

export type ExtractionPriority = "MustHave" | "NiceToHave" | "Unspecified";
export type ExtractionKind =
  | "Skill"
  | "Experience"
  | "Qualification"
  | "Language"
  | "Availability"
  | "Location"
  | "Other";
export type ExtractionSeniority = "Junior" | "Mid" | "Senior" | "Lead" | "Principal" | "Unspecified";

export interface JdExtractedRequirement {
  text: string;
  kind: ExtractionKind;
  priority: ExtractionPriority;
  minYears?: number | null;
  /** Verbatim JD quote backing the requirement; null when the model could not quote one. */
  evidenceSpan?: string | null;
  /** True when the evidence quote could not be verified verbatim — badged, never hidden. */
  inferred: boolean;
}

export interface JdExtraction {
  requirements: JdExtractedRequirement[];
  seniority: ExtractionSeniority;
  location?: string | null;
  /** The model's explicit "the JD is unclear about X" outlet — honesty, not filler. */
  ambiguities: string[];
}
