// What the 15 child-collection hooks put on the wire, and what they invalidate afterwards
// (EXP-80).
//
// Both halves are here because the hooks are now generated from one `childCrud` factory rather
// than hand-written fifteen times. A generator makes the shape uniform, which is the point — and
// it also makes a wrong path or a wrong key uniform, which is the risk. These tests pin the two
// things a reader can no longer see by scrolling the file.
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it } from "vitest";
import type { AxiosAdapter, InternalAxiosRequestConfig } from "axios";
import {
  useAddAvailability,
  useAddExperience,
  useAddExpertSkill,
  useAddLanguage,
  useAddQualification,
  useDeleteAvailability,
  useDeleteExperience,
  useDeleteExpertSkill,
  useDeleteLanguage,
  useDeleteQualification,
  useUpdateAvailability,
  useUpdateExperience,
  useUpdateExpertSkill,
  useUpdateLanguage,
  useUpdateQualification,
} from "./expertChildren";
import { http } from "./http";

const EXPERT = "11111111-1111-1111-1111-111111111111";
const ITEM = "22222222-2222-2222-2222-222222222222";

let sent: InternalAxiosRequestConfig | null = null;

const capture: AxiosAdapter = async (config) => {
  sent = config;
  return { status: 200, statusText: "OK", data: {}, headers: {}, config };
};

function wrapper(client: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  };
}

function freshClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
}

/** Runs one mutation against a capturing adapter and reports the request it made. */
async function requestFrom(
  hook: (expertId: string) => { mutateAsync: (vars: never) => Promise<unknown> },
  variables: unknown,
  client: QueryClient = freshClient(),
) {
  http.defaults.adapter = capture;
  const { result } = renderHook(() => hook(EXPERT), { wrapper: wrapper(client) });
  await result.current.mutateAsync(variables as never);
  await waitFor(() => expect(sent).not.toBeNull());
  return { method: sent!.method?.toUpperCase(), url: sent!.url, body: sent!.data };
}

afterEach(() => {
  http.defaults.adapter = undefined;
  sent = null;
});

describe("the child-collection hooks", () => {
  // Literal paths, not derived from the module under test: a generator that changed a segment
  // would still agree with itself.
  const wire: Array<[string, (id: string) => never, unknown, string, string]> = [
    ["add a skill", useAddExpertSkill as never, { skillId: "s", level: 3 }, "POST", `/experts/${EXPERT}/skills`],
    ["update a skill", useUpdateExpertSkill as never, { id: ITEM, skillId: "s", level: 3 }, "PUT", `/expert-skills/${ITEM}`],
    ["delete a skill", useDeleteExpertSkill as never, ITEM, "DELETE", `/expert-skills/${ITEM}`],
    ["add availability", useAddAvailability as never, { effectiveFrom: "2026-01-01" }, "POST", `/experts/${EXPERT}/availability`],
    ["update availability", useUpdateAvailability as never, { id: ITEM, effectiveFrom: "2026-01-01" }, "PUT", `/availability/${ITEM}`],
    ["delete availability", useDeleteAvailability as never, ITEM, "DELETE", `/availability/${ITEM}`],
    ["add a language", useAddLanguage as never, { language: "Dutch" }, "POST", `/experts/${EXPERT}/languages`],
    ["update a language", useUpdateLanguage as never, { id: ITEM, language: "Dutch" }, "PUT", `/languages/${ITEM}`],
    ["delete a language", useDeleteLanguage as never, ITEM, "DELETE", `/languages/${ITEM}`],
    ["add a qualification", useAddQualification as never, { title: "BSc" }, "POST", `/experts/${EXPERT}/qualifications`],
    ["update a qualification", useUpdateQualification as never, { id: ITEM, title: "BSc" }, "PUT", `/qualifications/${ITEM}`],
    ["delete a qualification", useDeleteQualification as never, ITEM, "DELETE", `/qualifications/${ITEM}`],
    ["add an experience", useAddExperience as never, { role: "Dev" }, "POST", `/experts/${EXPERT}/experiences`],
    ["update an experience", useUpdateExperience as never, { id: ITEM, role: "Dev" }, "PUT", `/experiences/${ITEM}`],
    ["delete an experience", useDeleteExperience as never, ITEM, "DELETE", `/experiences/${ITEM}`],
  ];

  it.each(wire)("%s hits the documented endpoint", async (_name, hook, variables, method, url) => {
    const request = await requestFrom(hook as never, variables);

    expect(request.method).toBe(method);
    expect(request.url).toBe(url);
  });

  it("sends an update without the id in the body — the id addresses the row, it is not a field", async () => {
    const request = await requestFrom(useUpdateLanguage as never, { id: ITEM, language: "Dutch" });

    expect(JSON.parse(request.body as string)).toEqual({ language: "Dutch" });
  });
});

describe("what a mutation refreshes", () => {
  // The three experience mutations used to invalidate ["experts", id, "cv"] explicitly, alongside
  // ["experts", id]. That second call was redundant — React Query matches query keys by prefix —
  // and EXP-80 removed it. These tests are what makes the removal safe: they watch the CV query
  // itself, so a future change to the key shape that breaks the prefix relationship fails here
  // rather than silently leaving a stale CV on screen.
  const cvKey = ["experts", EXPERT, "cv"];
  const detailKey = ["experts", EXPERT];

  async function cvInvalidatedBy(hook: (id: string) => never, variables: unknown) {
    const client = freshClient();
    client.setQueryData(cvKey, { markdown: "# CV" });
    client.setQueryData(detailKey, { id: EXPERT });
    expect(client.getQueryState(cvKey)?.isInvalidated).toBe(false);

    await requestFrom(hook as never, variables, client);

    return client.getQueryState(cvKey)?.isInvalidated;
  }

  it("refreshes the CV when an experience is added", async () => {
    expect(await cvInvalidatedBy(useAddExperience as never, { role: "Dev" })).toBe(true);
  });

  it("refreshes the CV when an experience is updated", async () => {
    expect(await cvInvalidatedBy(useUpdateExperience as never, { id: ITEM, role: "Dev" })).toBe(true);
  });

  it("refreshes the CV when an experience is deleted", async () => {
    expect(await cvInvalidatedBy(useDeleteExperience as never, ITEM)).toBe(true);
  });

  it("refreshes the CV when a qualification changes too — the detail key covers every child", async () => {
    // Not an accident of the experience hooks: every child mutation invalidates the same prefix,
    // and the CV hangs off it. Qualifications appear on the rendered CV as well.
    expect(await cvInvalidatedBy(useAddQualification as never, { title: "BSc" })).toBe(true);
  });

  it("leaves another expert's CV alone", async () => {
    const other = ["experts", "33333333-3333-3333-3333-333333333333", "cv"];
    const client = freshClient();
    client.setQueryData(other, { markdown: "# Someone else" });

    await requestFrom(useAddExperience as never, { role: "Dev" }, client);

    expect(client.getQueryState(other)?.isInvalidated).toBe(false);
  });
});
