// Bench & capability-gap report (P1T-104). Every number in `stats` is server-composed (a direct MCP
// roster call plus the proposals ledger); the markdown `answer` is model prose over those numbers —
// or a deterministic fallback summary when the model degraded, which `notes` says so.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface BenchNameCount {
  name: string;
  count: number;
}

export interface BenchProposalStats {
  total: number;
  pending: number;
  approved: number;
  rejected: number;
  recentJobDescriptions: string[];
  frequentCandidates: BenchNameCount[];
}

export interface BenchStats {
  activeEmployees: number;
  fullyAvailable: number;
  partiallyAvailable: number;
  fullyBooked: number;
  averageCapacityPercent: number;
  topTitles: BenchNameCount[];
  locations: BenchNameCount[];
  proposals?: BenchProposalStats | null;
}

export interface BenchReportResponse {
  answer: string;
  stats: BenchStats;
  notes: string[];
}

export function useBenchReport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => (await agentHttp.post<BenchReportResponse>("/bench-report", {})).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}
