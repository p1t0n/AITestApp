import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  agentHttp,
  useBenchReport,
  useCvTailoring,
  useInterviewKit,
  useJdMatch,
  useMatch,
  useResumeIngestion,
  useRosterQa,
  useShortlist,
} from "../index";

// The eight agent mutation hooks all run through `agentPost`, so what used to be eight copies of
// the same four lines is now one implementation — and the thing worth holding is what each hook
// still puts on the wire. Two properties the types cannot carry:
//
//   the URL and body — these go to the *agents* backend, and the ones whose argument is not the
//   request itself (bench's absent argument, ingestion's bare string) have to keep sending what
//   the server already parses;
//   the usage invalidation — every agent call spends tokens, so a run that does not invalidate
//   ["usage"] leaves the ledger on screen quietly wrong.

const ANY_RESULT = { answer: "ok" };

const CASES: {
  name: string;
  hook: () => { mutate: (req: never) => void; data?: unknown };
  variables: unknown;
  path: string;
  body: unknown;
}[] = [
  {
    name: "useMatch",
    hook: useMatch as never,
    variables: { expertId: "e1", jobDescription: "jd" },
    path: "/match",
    body: { expertId: "e1", jobDescription: "jd" },
  },
  {
    name: "useJdMatch",
    hook: useJdMatch as never,
    variables: { jobDescription: "jd", topK: 3 },
    path: "/match",
    body: { jobDescription: "jd", topK: 3 },
  },
  {
    name: "useInterviewKit",
    hook: useInterviewKit as never,
    variables: { expertId: "e1", jobDescription: "jd" },
    path: "/interview-kit",
    body: { expertId: "e1", jobDescription: "jd" },
  },
  {
    name: "useCvTailoring",
    hook: useCvTailoring as never,
    variables: { expertId: "e1", jobDescription: "jd" },
    path: "/cv-tailoring",
    body: { expertId: "e1", jobDescription: "jd" },
  },
  {
    name: "useShortlist",
    hook: useShortlist as never,
    variables: { jobDescription: "jd" },
    path: "/shortlist",
    body: { jobDescription: "jd" },
  },
  // Takes no argument at all, and the server still wants a JSON object body.
  {
    name: "useBenchReport",
    hook: useBenchReport as never,
    variables: undefined,
    path: "/bench-report",
    body: {},
  },
  {
    name: "useRosterQa",
    hook: useRosterQa as never,
    variables: { question: "who knows React?" },
    path: "/roster-qa",
    body: { question: "who knows React?" },
  },
  // The one hook whose argument is not the request: a bare string, wrapped by the hook.
  {
    name: "useResumeIngestion",
    hook: useResumeIngestion as never,
    variables: "Ada Lovelace, engineer",
    path: "/resume-ingestion",
    body: { resumeText: "Ada Lovelace, engineer" },
  },
];

let queryClient: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

afterEach(() => {
  vi.restoreAllMocks();
  queryClient.clear();
});

describe.each(CASES)("$name", ({ hook, variables, path, body }) => {
  it("posts to the agents backend and invalidates the usage ledger", async () => {
    const post = vi.spyOn(agentHttp, "post").mockResolvedValue({ data: ANY_RESULT } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(hook, { wrapper });
    result.current.mutate(variables as never);

    await waitFor(() => expect(result.current.data).toEqual(ANY_RESULT));
    expect(post).toHaveBeenCalledExactlyOnceWith(path, body);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["usage"] });
  });

  it("leaves the usage ledger alone when the call fails", async () => {
    vi.spyOn(agentHttp, "post").mockRejectedValue(new Error("boom"));
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(hook as never, { wrapper });
    (result.current as { mutate: (v: never) => void }).mutate(variables as never);

    await waitFor(() => expect((result.current as { isError: boolean }).isError).toBe(true));
    expect(invalidate).not.toHaveBeenCalled();
  });
});

// Two hooks invalidate more than the ledger, and in both cases the extra key is the whole point of
// the surface rather than a nicety — so it is asserted beside the shared behaviour, not inside it.

describe("the surfaces that invalidate more than usage", () => {
  it("useResumeIngestion invalidates the roster it just wrote to", async () => {
    vi.spyOn(agentHttp, "post").mockResolvedValue({ data: ANY_RESULT } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useResumeIngestion(), { wrapper });
    result.current.mutate("Ada Lovelace, engineer");

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["experts"] });
  });

  it("useRosterQa invalidates the conversation index exactly, never the open transcript", async () => {
    vi.spyOn(agentHttp, "post").mockResolvedValue({ data: ANY_RESULT } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useRosterQa(), { wrapper });
    result.current.mutate({ question: "who knows React?" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: ["roster-qa-conversations"],
      exact: true,
    });
  });
});
