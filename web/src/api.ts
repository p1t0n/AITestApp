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
  Experience,
  SaveEmployee,
  SkillDto,
} from "./types";
import { clearSession, getToken, setSession } from "./auth/session";
import { performAuthentication, performRegistration } from "./auth/webauthn";
import { postSse } from "./sse";

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
// Both take the same input. Match returns { answer } (markdown prose). CV Tailoring returns the
// hybrid contract: the same markdown answer plus vetted per-achievement rewrites keyed to CV rows
// (empty when the model's structured output could not be validated — the degrade path).

export interface AgentJobRequest {
  employeeId: string;
  jobDescription: string;
}

export interface AgentAnswer {
  answer: string;
}

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
 * plain Web-API edit with the user's own session, exactly like a manual edit. The Web API has no
 * per-achievement endpoint, so we go through the experience update: fetch the employee, swap the
 * one bullet's text, and PUT the experience back otherwise unchanged.
 */
export function useApplyRewrite() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: ApplyRewriteInput) => {
      const employee = (await http.get<EmployeeDetail>(`/employees/${input.employeeId}`)).data;
      const exp = employee.experiences.find((x) => x.id === input.experienceId);
      if (!exp) throw new Error("This experience no longer exists on the employee's profile.");
      // Prefer the id; fall back to the original text (the experience PUT regenerates achievement
      // ids, so applying a sibling rewrite first leaves this card's id stale), then to the
      // rewritten text (already applied — the PUT below rewrites identical text, a no-op).
      const target =
        exp.achievements.find((a) => a.id === input.achievementId) ??
        exp.achievements.find((a) => a.text === input.original) ??
        exp.achievements.find((a) => a.text === input.rewritten);
      if (!target) throw new Error("This bullet no longer exists on the employee's profile.");
      const dto = {
        company: exp.company,
        title: exp.title,
        location: exp.location,
        startDate: exp.startDate,
        endDate: exp.endDate,
        summary: exp.summary,
        achievements: exp.achievements.map((a) => ({
          order: a.order,
          text: a.id === target.id ? input.rewritten : a.text,
        })),
        skillIds: exp.skills.map((s) => s.skillId),
      };
      return (await http.put<Experience>(`/experiences/${input.experienceId}`, dto)).data;
    },
    // Prefix-matches both the detail query (["employees", id]) and the CV query
    // (["employees", id, "cv"]), so open detail/CV views refetch.
    onSuccess: (_data, input) =>
      qc.invalidateQueries({ queryKey: ["employees", input.employeeId] }),
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

// ---- Shortlist agent ----
// Unlike the prose agents above, shortlist returns a pinned structured contract: the requirement
// strings the model extracted from the JD plus coverage-ranked candidates with per-requirement
// evidence. All filters are optional; omitted fields fall back to server defaults.

export interface ShortlistRequest {
  jobDescription: string;
  availableOn?: string; // ISO date (yyyy-MM-dd)
  skillIds?: string[];
  location?: string;
  minYears?: number;
  topK?: number;
}

export interface ShortlistCoverage {
  matched: number;
  total: number;
}

export interface ShortlistRequirementItem {
  text: string;
  matched: boolean;
  snippet?: string; // omitted by the server when there is no evidence
}

export interface ShortlistCandidate {
  employeeId: string;
  name: string;
  title: string;
  score: number;
  coverage: ShortlistCoverage;
  requirements: ShortlistRequirementItem[];
  rationale: string;
}

export interface ShortlistResponse {
  requirements: string[];
  candidates: ShortlistCandidate[];
}

export function useShortlist() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: ShortlistRequest) =>
      (await agentHttp.post<ShortlistResponse>("/shortlist", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}

// ---- Staffing agent (SSE) ----
// POST /agents/staffing streams Server-Sent Events (P1T-76): step/stepFailed progress frames for a
// stepper UI, then exactly one terminal frame — the full report (P1T-71) or a problem-style error.
// Pre-stream failures (400/401/429) surface as thrown SseHttpError before any event arrives. The
// axios clients buffer whole responses, so this one rides the fetch-based SSE helper instead.

export interface StaffingRequest {
  jobDescription: string;
  availableOn?: string; // ISO date (yyyy-MM-dd)
  skillIds?: string[];
  location?: string;
  minYears?: number;
  matchTop?: number; // 1..5, server default 3
}

export type StaffingStage = "shortlist" | "match" | "narrative";
export type StaffingStepStatus = "started" | "completed" | "failed";

/** One `step`/`stepFailed` frame. Match-stage frames carry the candidate and k/N counters;
 * `error` is set only on `stepFailed` (the run continues under the degrade policy). */
export interface StaffingStepEvent {
  stage: StaffingStage;
  status: StaffingStepStatus;
  candidate?: { employeeId: string; name: string };
  completedCount?: number;
  totalCount?: number;
  error?: string;
}

export type StaffingMatchStatus = "completed" | "failed" | "skipped";

/** One candidate's match-step result. Score/band are parsed from the answer markdown and can be
 * null even when completed (the markdown ships regardless); `error` is set only on failure. */
export interface StaffingMatchResult {
  status: StaffingMatchStatus;
  score?: number | null;
  band?: string | null;
  answer?: string | null;
  error?: string | null;
}

export interface StaffingReportCandidate {
  employeeId: string;
  name: string;
  title: string;
  shortlist: {
    score: number;
    coverage: ShortlistCoverage;
    requirements: ShortlistRequirementItem[];
  };
  match: StaffingMatchResult;
  rationale: string;
}

/** The pinned staffing report (P1T-71). `recommendation` is absent when the narrative degraded;
 * `degraded` + `notes` explain any partial results. */
export interface StaffingReport {
  requirements: string[];
  candidates: StaffingReportCandidate[];
  recommendation?: { employeeId: string; narrative: string } | null;
  degraded: boolean;
  notes: string[];
}

/** The terminal `error` frame (failed shortlist or an unexpected fault). */
export interface StaffingTerminalError {
  title: string;
  detail: string;
}

export interface StaffingRunHandlers {
  /** Every `step` and `stepFailed` frame, in order (`stepFailed` arrives with status "failed"). */
  onStep: (event: StaffingStepEvent) => void;
  /** The terminal report frame. */
  onReport: (report: StaffingReport) => void;
  /** The terminal error frame. */
  onError: (error: StaffingTerminalError) => void;
}

/**
 * Runs one staffing pipeline over SSE. Resolves when the stream closes (after a terminal frame);
 * rejects with SseHttpError on pre-stream HTTP failures (e.g. the 429 cap body) and with the
 * abort error when `signal` cancels the run mid-stream.
 */
export async function runStaffing(
  req: StaffingRequest,
  handlers: StaffingRunHandlers,
  signal?: AbortSignal,
): Promise<void> {
  await postSse(
    "/agents/staffing",
    req,
    (message) => {
      switch (message.event) {
        case "step":
        case "stepFailed":
          handlers.onStep(JSON.parse(message.data) as StaffingStepEvent);
          break;
        case "report":
          handlers.onReport(JSON.parse(message.data) as StaffingReport);
          break;
        case "error":
          handlers.onError(JSON.parse(message.data) as StaffingTerminalError);
          break;
      }
    },
    signal,
  );
}

// ---- Resume ingestion (P1T-96) ----

export interface IngestionCreated {
  languages: number;
  skills: number;
  qualifications: number;
  experiences: number;
}

/** The composed ingestion result: deterministic fields from captured tool results; proposals are
 * catalog-unmatched skill names awaiting a human decision. */
export interface IngestionResponse {
  employeeId: string;
  created: IngestionCreated;
  proposals: string[];
  notes: string[];
  duplicateWarning: string | null;
  degraded: boolean;
}

export function useResumeIngestion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (resumeText: string) =>
      (await agentHttp.post<IngestionResponse>("/resume-ingestion", { resumeText })).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["usage"] });
      qc.invalidateQueries({ queryKey: ["employees"] });
    },
  });
}

/** The human publication gate: flips a Draft to Active (requires a valid email server-side). */
export function usePromoteEmployee() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (await http.post<EmployeeDetail>(`/employees/${id}/promote`)).data,
    onSuccess: (_, id) => {
      qc.invalidateQueries({ queryKey: ["employees"] });
      qc.invalidateQueries({ queryKey: ["employees", id] });
    },
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
