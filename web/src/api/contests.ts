// Contesting an automated score (P1T-189) — the Art. 22(3) safeguards. We concede the scoring is
// automated and rely on Art. 22(2)(a), which makes these obligations rather than good practice: a
// human looks, the person may say why, and the outcome is recorded.
//
// Query keys, invalidated by prefix:
//   ["contests"]   the Service Manager's queue of scores waiting for a human
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { http } from "./http";

export type ContestOutcome = "upheld" | "overturned";

export interface ContestQueueItem {
  scoringCandidateId: string;
  expertId: string;
  expertName: string;
  jobDescription: string;
  score: number | null;
  band: string | null;
  rationale: string | null;
  /** What the person said about the score. Read this before the score. */
  view: string | null;
  contestedAt: string;
}

export interface ContestReview {
  scoringCandidateId: string;
  outcome: ContestOutcome;
  response: string | null;
  reviewedAt: string;
  reviewedByUserId: string | null;
}

/** Scores waiting for a human, oldest first. Service Manager only. */
export function useContestQueue() {
  return useQuery({
    queryKey: ["contests"],
    queryFn: async () => (await http.get<ContestQueueItem[]>("/contests")).data,
  });
}

/**
 * Asks for a human to look at one of your own scores. The view is optional: asking is a right on
 * its own, and requiring an explanation first would be a toll on it.
 */
export function useContestScore() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ scoringCandidateId, view }: { scoringCandidateId: string; view?: string }) =>
      (await http.post<ContestQueueItem>("/contests", { scoringCandidateId, view })).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["contests"] });
      qc.invalidateQueries({ queryKey: ["me", "access"] });
    },
  });
}

/** Records that a human looked, and what they said back. */
export function useReviewContest() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (
      { scoringCandidateId, outcome, response }:
        { scoringCandidateId: string; outcome: ContestOutcome; response?: string },
    ) =>
      (await http.post<ContestReview>(
        `/contests/${scoringCandidateId}/review`, { outcome, response })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["contests"] }),
  });
}
