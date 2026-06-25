import axios from "axios";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import type {
  Category,
  CategoryNode,
  Cv,
  EmployeeDetail,
  EmployeeSkill,
  EmployeeSummary,
  SaveEmployee,
  SkillDto,
} from "./types";
import { getToken, setSession } from "./auth/session";
import { performRegistration } from "./auth/webauthn";

export const http = axios.create({ baseURL: "/api" });

// Roster Q&A agent lives on its own sibling service (proxied at /agents), not the CRUD API.
export const agentHttp = axios.create({ baseURL: "/agents" });

// Attach the session token (if any) to every request on both services. The token is issued by the
// Web host and validated by both Web and Agents (shared signing key).
for (const client of [http, agentHttp]) {
  client.interceptors.request.use((config) => {
    const token = getToken();
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  });
}

// ---- Auth (passwordless: passkey signup) ----

export interface AuthSession {
  token: string;
  expiresAt: string;
  userId: string;
  email: string;
}

interface SignupBeginResponse {
  ceremonyId: string;
  optionsJson: string;
}

/**
 * Self-serve signup. Two-step WebAuthn registration: the server returns credential-creation
 * options, the browser drives the authenticator, and the server verifies + creates the account,
 * returning a session token. The control word is the account's recovery secret (P1T-20).
 */
export function useSignup() {
  return useMutation({
    mutationFn: async (input: { email: string; controlWord: string }): Promise<AuthSession> => {
      const begin = (await http.post<SignupBeginResponse>("/auth/signup/begin", input)).data;
      const attestation = await performRegistration(begin.optionsJson);
      const session = (
        await http.post<AuthSession>("/auth/signup/complete", {
          ceremonyId: begin.ceremonyId,
          attestation,
        })
      ).data;
      setSession(session.token);
      return session;
    },
  });
}

// ---- Roster Q&A agent ----

export interface RosterQaResponse {
  answer: string;
}

/**
 * Ask the Roster Q&A agent a single question. The endpoint is stateless today (issue #15);
 * a threadId will be threaded through here once threaded sessions land (issue #16).
 */
export function useRosterQa() {
  return useMutation({
    mutationFn: async (question: string) =>
      (await agentHttp.post<RosterQaResponse>("/roster-qa", { question })).data,
  });
}

// ---- CV Tailoring & Match agents ----
// Both take the same input and return { answer } (markdown prose). CV Tailoring rewrites a CV for
// a job description; Match assesses fit (gap analysis + rubric). They hit their own endpoints.

export interface AgentJobRequest {
  employeeId: string;
  jobDescription: string;
}

export interface AgentAnswer {
  answer: string;
}

export function useCvTailoring() {
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<AgentAnswer>("/cv-tailoring", req)).data,
  });
}

export function useMatch() {
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<AgentAnswer>("/match", req)).data,
  });
}

// ---- Employees ----

export function useEmployees() {
  return useQuery({
    queryKey: ["employees"],
    queryFn: async () => (await http.get<EmployeeSummary[]>("/employees")).data,
  });
}

export function useEmployee(id: string) {
  return useQuery({
    queryKey: ["employees", id],
    queryFn: async () => (await http.get<EmployeeDetail>(`/employees/${id}`)).data,
    enabled: !!id,
  });
}

export function useCv(id: string) {
  return useQuery({
    queryKey: ["employees", id, "cv"],
    queryFn: async () => (await http.get<Cv>(`/employees/${id}/cv`)).data,
    enabled: !!id,
  });
}

export function useCreateEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveEmployee) =>
      (await http.post<EmployeeDetail>("/employees", dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees"] }),
  });
}

export function useUpdateEmployee(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveEmployee) =>
      (await http.put<EmployeeDetail>(`/employees/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees"] });
      qc.invalidateQueries({ queryKey: ["employees", id] });
    },
  });
}

export function useDeleteEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/employees/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees"] }),
  });
}

// ---- Employee skills ----

export function useAddEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { skillId: string; level: string; yearsExperience: number }) =>
      (await http.post<EmployeeSkill>(`/employees/${employeeId}/skills`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (employeeSkillId: string) =>
      http.delete(`/employee-skills/${employeeSkillId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

// ---- Availability ----

export function useAddAvailability(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { effectiveFrom: string; capacityPercent: number }) =>
      (await http.post(`/employees/${employeeId}/availability`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteAvailability(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (entryId: string) => http.delete(`/availability/${entryId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

// ---- Catalog ----

export function useCategories() {
  return useQuery({
    queryKey: ["categories"],
    queryFn: async () => (await http.get<Category[]>("/catalog/categories")).data,
  });
}

export function useCategoryTree() {
  return useQuery({
    queryKey: ["categories", "tree"],
    queryFn: async () => (await http.get<CategoryNode[]>("/catalog/categories/tree")).data,
  });
}

export function useSkills() {
  return useQuery({
    queryKey: ["skills"],
    queryFn: async () => (await http.get<SkillDto[]>("/catalog/skills")).data,
  });
}

export function useCreateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { name: string; parentId: string | null }) =>
      (await http.post<Category>("/catalog/categories", dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["categories"] }),
  });
}

export function useUpdateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: { id: string; name: string; parentId: string | null }) =>
      (await http.put<Category>(`/catalog/categories/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["categories"] });
      qc.invalidateQueries({ queryKey: ["skills"] });
    },
  });
}

export function useDeleteCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/catalog/categories/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["categories"] }),
  });
}

export function useCreateSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: { name: string; categoryId: string }) =>
      (await http.post<SkillDto>("/catalog/skills", dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useUpdateSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: { id: string; name: string; categoryId: string }) =>
      (await http.put<SkillDto>(`/catalog/skills/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function useDeleteSkill() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/catalog/skills/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["skills"] });
      qc.invalidateQueries({ queryKey: ["categories"] });
    },
  });
}

export function apiErrorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data as { detail?: string; title?: string } | undefined;
    return data?.detail ?? data?.title ?? err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}
