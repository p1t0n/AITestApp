import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { http, useApplyRewrite, type ApplyRewriteInput } from "./api";
import type { EmployeeDetail } from "./types";

// The design rule (P1T-62) says the agent never writes: applying a tailoring rewrite is a plain
// Web-API edit with the user's own session. The Web API has no per-achievement endpoint, so the
// hook goes through the experience update: GET the employee, swap the one bullet's text, and
// PUT /experiences/{id} back otherwise unchanged.

const EMPLOYEE_ID = "11111111-2222-3333-4444-555555555555";
const EXPERIENCE_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const ACHIEVEMENT_1 = "a1a1a1a1-1111-1111-1111-111111111111";
const ACHIEVEMENT_2 = "a2a2a2a2-2222-2222-2222-222222222222";
const SKILL_A = "51111111-1111-1111-1111-111111111111";
const SKILL_B = "52222222-2222-2222-2222-222222222222";

function employeeDetail(): EmployeeDetail {
  return {
    id: EMPLOYEE_ID,
    firstName: "Ada",
    lastName: "Lovelace",
    title: "Senior Engineer",
    email: "ada@example.com",
    phone: null,
    location: null,
    summary: null,
    photoUrl: null,
    currentCapacityPercent: 100,
    spokenLanguages: [],
    availabilityEntries: [],
    skills: [],
    qualifications: [],
    experiences: [
      {
        id: EXPERIENCE_ID,
        company: "Analytical Engines Ltd",
        title: "Engineer",
        location: "London",
        startDate: "2020-01-01",
        endDate: null,
        summary: "Compute things.",
        achievements: [
          { id: ACHIEVEMENT_1, order: 1, text: "Worked on React apps" },
          { id: ACHIEVEMENT_2, order: 2, text: "Did some performance work" },
        ],
        skills: [
          { id: "e1", skillId: SKILL_A, skillName: "React" },
          { id: "e2", skillId: SKILL_B, skillName: "TypeScript" },
        ],
      },
    ],
  };
}

const INPUT: ApplyRewriteInput = {
  employeeId: EMPLOYEE_ID,
  experienceId: EXPERIENCE_ID,
  achievementId: ACHIEVEMENT_2,
  original: "Did some performance work",
  rewritten: "Cut page-load time 45% by code-splitting and memoizing hot paths",
};

let queryClient: QueryClient;

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("useApplyRewrite", () => {
  it("PUTs the full experience with only the target achievement's text replaced", async () => {
    vi.spyOn(http, "get").mockResolvedValue({ data: employeeDetail() } as never);
    const put = vi.spyOn(http, "put").mockResolvedValue({ data: {} } as never);

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    await result.current.mutateAsync(INPUT);

    expect(http.get).toHaveBeenCalledWith(`/employees/${EMPLOYEE_ID}`);
    expect(put).toHaveBeenCalledWith(`/experiences/${EXPERIENCE_ID}`, {
      company: "Analytical Engines Ltd",
      title: "Engineer",
      location: "London",
      startDate: "2020-01-01",
      endDate: null,
      summary: "Compute things.",
      achievements: [
        { order: 1, text: "Worked on React apps" },
        { order: 2, text: "Cut page-load time 45% by code-splitting and memoizing hot paths" },
      ],
      skillIds: [SKILL_A, SKILL_B],
    });
  });

  it("invalidates the employee's queries on success (detail + CV by prefix)", async () => {
    vi.spyOn(http, "get").mockResolvedValue({ data: employeeDetail() } as never);
    vi.spyOn(http, "put").mockResolvedValue({ data: {} } as never);
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    await result.current.mutateAsync(INPUT);

    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["employees", EMPLOYEE_ID] }),
    );
  });

  it("falls back to matching by original text when the achievement id is stale", async () => {
    // A previous apply in the same experience regenerated all achievement ids (the experience PUT
    // replaces children), so the rewrite's achievementId no longer exists — but the bullet does.
    const detail = employeeDetail();
    detail.experiences[0].achievements = [
      { id: "f1f1f1f1-0000-0000-0000-000000000001", order: 1, text: "Worked on React apps" },
      { id: "f2f2f2f2-0000-0000-0000-000000000002", order: 2, text: "Did some performance work" },
    ];
    vi.spyOn(http, "get").mockResolvedValue({ data: detail } as never);
    const put = vi.spyOn(http, "put").mockResolvedValue({ data: {} } as never);

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    await result.current.mutateAsync(INPUT);

    expect(put).toHaveBeenCalledWith(
      `/experiences/${EXPERIENCE_ID}`,
      expect.objectContaining({
        achievements: [
          { order: 1, text: "Worked on React apps" },
          { order: 2, text: "Cut page-load time 45% by code-splitting and memoizing hot paths" },
        ],
      }),
    );
  });

  it("re-applying already-applied text is a server-side no-op (matches the rewritten bullet)", async () => {
    const detail = employeeDetail();
    detail.experiences[0].achievements = [
      { id: "f1f1f1f1-0000-0000-0000-000000000001", order: 1, text: "Worked on React apps" },
      { id: "f2f2f2f2-0000-0000-0000-000000000002", order: 2, text: INPUT.rewritten },
    ];
    vi.spyOn(http, "get").mockResolvedValue({ data: detail } as never);
    const put = vi.spyOn(http, "put").mockResolvedValue({ data: {} } as never);

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    await result.current.mutateAsync(INPUT);

    expect(put).toHaveBeenCalledWith(
      `/experiences/${EXPERIENCE_ID}`,
      expect.objectContaining({
        achievements: [
          { order: 1, text: "Worked on React apps" },
          { order: 2, text: INPUT.rewritten },
        ],
      }),
    );
  });

  it("rejects without writing when the bullet cannot be found at all", async () => {
    const detail = employeeDetail();
    detail.experiences[0].achievements = [
      { id: "f1f1f1f1-0000-0000-0000-000000000001", order: 1, text: "Something else entirely" },
    ];
    vi.spyOn(http, "get").mockResolvedValue({ data: detail } as never);
    const put = vi.spyOn(http, "put").mockResolvedValue({ data: {} } as never);

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    await expect(result.current.mutateAsync(INPUT)).rejects.toThrow(/no longer exists/i);
    expect(put).not.toHaveBeenCalled();
  });

  it("rejects without writing when the experience cannot be found", async () => {
    const detail = employeeDetail();
    detail.experiences = [];
    vi.spyOn(http, "get").mockResolvedValue({ data: detail } as never);
    const put = vi.spyOn(http, "put").mockResolvedValue({ data: {} } as never);

    const { result } = renderHook(() => useApplyRewrite(), { wrapper });
    await expect(result.current.mutateAsync(INPUT)).rejects.toThrow(/no longer exists/i);
    expect(put).not.toHaveBeenCalled();
  });
});
