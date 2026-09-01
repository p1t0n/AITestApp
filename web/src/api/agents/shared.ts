// Contracts more than one agent surface speaks.

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
