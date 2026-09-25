// Which model answers where (EXP-31). A per-deployment fact, not a per-user one: it changes when
// somebody edits `Ai:*` and restarts the Agents host, which no open tab can observe. So it is
// fetched once and then left alone — `staleTime: Infinity` — rather than refetched on every focus
// like the token ledger, which really does move under the tab.
import { useQuery } from "@tanstack/react-query";
import { agentHttp } from "../http";

export interface AgentModels {
  /** The configured chat provider's name, e.g. `Gemini` or `AzureFoundry`. */
  provider: string;
  /**
   * Dock surface id → the distinct, sorted models the agents behind it resolve to. Keyed by the
   * same ids `Surface` in `AgentWidget.tsx` uses; a surface backed by several agents (Staffing runs
   * four) lists each distinct model once. Typed as a loose record rather than as `Record<Surface,
   * …>` on purpose: the server's list is the authority, and a surface it has not heard of should
   * read as "nothing to say" rather than as a type error at the import.
   */
  surfaces: Record<string, string[] | undefined>;
}

/** The models the agents run on. One fetch per session; failure is silent by design — the caption
 * this feeds is an aside, never something a surface waits on. */
export function useAgentModels() {
  return useQuery({
    queryKey: ["agent-models"],
    queryFn: async () => (await agentHttp.get<AgentModels>("/models")).data,
    // Held by `models.test.tsx`: a remount must not refetch. The default (0) would re-ask on
    // every dock open and every window focus, for an answer that only a host restart can change.
    staleTime: Infinity,
  });
}
