// Shortlist. Unlike the prose agents, it returns a pinned structured contract: the requirement
// strings the model extracted from the JD plus coverage-ranked candidates with per-requirement
// evidence. All filters are optional; omitted fields fall back to server defaults.
//
// The coverage and requirement-item shapes are re-used by the staffing report, which embeds a
// shortlist slice per candidate — so they are exported from here rather than duplicated.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";
import type { JdExtraction } from "./shared";

export interface ShortlistRequest {
  jobDescription: string;
  availableOn?: string; // ISO date (yyyy-MM-dd)
  skillIds?: string[];
  location?: string;
  minYears?: number;
  topK?: number;
}

export interface ShortlistCoverage {
  matched: number;
  total: number;
}

export interface ShortlistRequirementItem {
  text: string;
  matched: boolean;
  snippet?: string; // omitted by the server when there is no evidence
}

export interface ShortlistCandidate {
  expertId: string;
  name: string;
  title: string;
  score: number;
  coverage: ShortlistCoverage;
  requirements: ShortlistRequirementItem[];
  rationale: string;
}

export interface ShortlistResponse {
  requirements: string[];
  candidates: ShortlistCandidate[];
  extraction?: JdExtraction | null;
}

export function useShortlist() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: ShortlistRequest) =>
      (await agentHttp.post<ShortlistResponse>("/shortlist", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}
