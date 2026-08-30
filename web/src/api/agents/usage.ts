// The token ledger. Caps are enforced server-side *before* a request (P1T-24), so this is a
// read-only view of what has been spent — never a gate.
//
// Every agent mutation in this folder invalidates ["usage"]. That is what keeps the dock's Usage
// tab honest without polling: the ledger updates the moment any agent call returns.
import { useQuery } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface WindowUsage {
  window: "daily" | "weekly" | "monthly";
  used: number;
  cap: number;
  resetAt: string;
  exceeded: boolean;
}

export interface AgentBreakdown {
  agentName: string;
  totalTokens: number;
}

export interface UsageSnapshot {
  daily: WindowUsage;
  weekly: WindowUsage;
  monthly: WindowUsage;
  byAgent: AgentBreakdown[];
}

/** The current user's token usage across all windows + per-agent breakdown. */
export function useUsage() {
  return useQuery({
    queryKey: ["usage"],
    queryFn: async () => (await agentHttp.get<UsageSnapshot>("/usage")).data,
  });
}
