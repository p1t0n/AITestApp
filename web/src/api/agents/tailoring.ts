// CV Tailoring returns the hybrid contract (P1T-62): the same markdown answer the prose agents
// give, plus vetted per-achievement rewrites keyed to CV rows — empty when the model's structured
// output could not be validated, which is the degrade path.
//
// Applying a rewrite is a plain Web-API edit under the user's own session. The agent never writes.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { Achievement } from "../../types";
import { agentHttp, http } from "../http";
import type { AgentJobRequest } from "./shared";

export interface TailoringRewrite {
  experienceId: string;
  achievementId: string;
  original: string;
  rewritten: string;
}

export interface CvTailoringResponse {
  answer: string;
  rewrites: TailoringRewrite[];
}

export function useCvTailoring() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<CvTailoringResponse>("/cv-tailoring", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}

/** A tailoring rewrite plus the employee it belongs to — everything Apply needs. */
export interface ApplyRewriteInput extends TailoringRewrite {
  employeeId: string;
}

/**
 * Apply a tailoring rewrite to the employee's profile. The agent never writes (P1T-62): this is a
 * plain Web-API edit with the user's own session, exactly like a manual edit. One PATCH per
 * bullet (P1T-90) — ids and sibling bullets stay untouched, so concurrent applies can't clobber
 * each other.
 */
export function useApplyRewrite() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: ApplyRewriteInput) =>
      (await http.patch<Achievement>(`/achievements/${input.achievementId}`, { text: input.rewritten }))
        .data,
    // Prefix-matches both the detail query (["employees", id]) and the CV query
    // (["employees", id, "cv"]), so open detail/CV views refetch.
    onSuccess: (_data, input) =>
      qc.invalidateQueries({ queryKey: ["employees", input.employeeId] }),
  });
}
