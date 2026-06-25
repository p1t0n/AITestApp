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
import { clearSession, getToken, setSession } from "./auth/session";
import { performAuthentication, performRegistration } from "./auth/webauthn";

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

interface CeremonyBeginResponse {
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
      const begin = (await http.post<CeremonyBeginResponse>("/auth/signup/begin", input)).data;
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

/**
 * Passkey sign-in. The server returns assertion options scoped to the email's registered
 * credentials; the browser signs the challenge and the server verifies it, returning a session
 * token. "No passkey on this device" surfaces as a server error pointing to recovery.
 */
export function useSignin() {
  return useMutation({
    mutationFn: async (input: { email?: string }): Promise<AuthSession> => {
      const begin = (
        await http.post<CeremonyBeginResponse>("/auth/signin/begin", {
          email: input.email?.trim() || null,
        })
      ).data;
      const assertion = await performAuthentication(begin.optionsJson);
      const session = (
        await http.post<AuthSession>("/auth/signin/complete", {
          ceremonyId: begin.ceremonyId,
          assertion,
        })
      ).data;
      setSession(session.token);
      return session;
    },
  });
}

/**
 * Account recovery. Verifies email + control word, then registers a NEW passkey for the existing
 * account (the old device's passkey is left intact). Signs the user in on success.
 */
export function useRecover() {
  return useMutation({
    mutationFn: async (input: { email: string; controlWord: string }): Promise<AuthSession> => {
      const begin = (await http.post<CeremonyBeginResponse>("/auth/recover/begin", input)).data;
      const attestation = await performRegistration(begin.optionsJson);
      const session = (
        await http.post<AuthSession>("/auth/recover/complete", {
          ceremonyId: begin.ceremonyId,
          attestation,
        })
      ).data;
      setSession(session.token);
      return session;
    },
  });
}

/** Clears the local session. Returns true if a session was present. */
export function signOut(): boolean {
  const had = getToken() !== null;
  clearSession();
  return had;
}

/** Whether a session token is currently stored (not a validity check). */
export function isSignedIn(): boolean {
  return getToken() !== null;
}

// ---- User management (flat roles: any signed-in user can manage any user) ----

export type UserStatus = "Active" | "Deactivated";

export interface UserSummary {
  id: string;
  email: string;
  status: UserStatus;
  dailyTokenCap: number | null;
  weeklyTokenCap: number | null;
  monthlyTokenCap: number | null;
  passkeyCount: number;
  createdAt: string;
}

export interface UpdateUser {
  email: string;
  status: UserStatus;
  dailyTokenCap: number | null;
  weeklyTokenCap: number | null;
  monthlyTokenCap: number | null;
}

export function useUsers() {
  return useQuery({
    queryKey: ["users"],
    queryFn: async () => (await http.get<UserSummary[]>("/users")).data,
  });
}

export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: { id: string } & UpdateUser) =>
      (await http.put<UserSummary>(`/users/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

export function useDeleteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/users/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

// ---- AI usage & caps ----

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

// ---- Roster Q&A agent ----

export interface RosterQaResponse {
  answer: string;
}

/**
 * Ask the Roster Q&A agent a single question. The endpoint is stateless today (issue #15);
 * a threadId will be threaded through here once threaded sessions land (issue #16).
 */
export function useRosterQa() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (question: string) =>
      (await agentHttp.post<RosterQaResponse>("/roster-qa", { question })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
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
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<AgentAnswer>("/cv-tailoring", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}

export function useMatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<AgentAnswer>("/match", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
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
    const data = err.response?.data as { error?: string; detail?: string; title?: string } | undefined;
    return data?.error ?? data?.detail ?? data?.title ?? err.message;
  }
  return err instanceof Error ? err.message : "Unknown error";
}
