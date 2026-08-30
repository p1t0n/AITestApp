import axios from "axios";
import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import type {
  Achievement,
  AvailabilityEntry,
  Category,
  CategoryNode,
  Cv,
  EmployeeDetail,
  EmployeeSkill,
  EmployeeSummary,
  Experience,
  Qualification,
  SaveAvailabilityEntry,
  SaveEmployee,
  SaveEmployeeSkill,
  SaveExperience,
  SaveQualification,
  SaveSpokenLanguage,
  SkillDto,
  SpokenLanguage,
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
  /** The conversation to continue. A returned id differing from the one sent means the server
   * started a fresh thread (expired/unknown) — the prior context is gone. */
  threadId: string;
}

export interface RosterQaInput {
  question: string;
  threadId?: string;
}

/** Ask the Roster Q&A agent. Pass the last response's threadId to continue the conversation. */
export function useRosterQa() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RosterQaInput) =>
      (await agentHttp.post<RosterQaResponse>("/roster-qa", input)).data,
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

export function useMatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<AgentAnswer>("/match", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}

// ---- JD-only match mode (P1T-103) ----
// POST /agents/match without an employeeId: shortlist retrieval picks the top candidates, the
// match run fans out per candidate. Failed entries degrade in place (status "failed" + error).

export interface JdMatchResult {
  employeeId: string;
  name: string;
  title: string;
  retrievalScore: number;
  status: "completed" | "failed";
  score?: number | null;
  band?: string | null;
  answer?: string | null;
  error?: string | null;
}

export interface JdMatchResponse {
  requirements: string[];
  results: JdMatchResult[];
}

export function useJdMatch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: { jobDescription: string; topK?: number }) =>
      (await agentHttp.post<JdMatchResponse>("/match", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["usage"] }),
  });
}

// ---- Bench report agent (P1T-104) ----
// Every number in `stats` is server-composed (direct MCP roster call + the proposals ledger);
// the markdown `answer` is model prose over those numbers — or a deterministic fallback summary
// when the model degraded (see `notes`).

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

// ---- Interview kit agent (P1T-102) ----
// Same input as Match/Tailoring; returns the markdown kit plus vetted structured questions.
// `evidence` is present only when the server verified the quote verbatim against the CV.

export interface InterviewQuestion {
  question: string;
  probes?: string | null;
  evidence?: string | null;
}

export interface InterviewKitResponse {
  answer: string;
  questions: InterviewQuestion[];
}

export function useInterviewKit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: AgentJobRequest) =>
      (await agentHttp.post<InterviewKitResponse>("/interview-kit", req)).data,
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

// ---- JD extraction (P1T-117/120) ----
// The structured reading of the JD that fed retrieval: priorities, evidence spans, inferred
// badges, ambiguities. Additive on shortlist/staffing responses; absent on degraded runs.

export type ExtractionPriority = "MustHave" | "NiceToHave" | "Unspecified";
export type ExtractionKind =
  | "Skill"
  | "Experience"
  | "Qualification"
  | "Language"
  | "Availability"
  | "Location"
  | "Other";
export type ExtractionSeniority = "Junior" | "Mid" | "Senior" | "Lead" | "Principal" | "Unspecified";

export interface JdExtractedRequirement {
  text: string;
  kind: ExtractionKind;
  priority: ExtractionPriority;
  minYears?: number | null;
  /** Verbatim JD quote backing the requirement; null when the model could not quote one. */
  evidenceSpan?: string | null;
  /** True when the evidence quote could not be verified verbatim — badged, never hidden. */
  inferred: boolean;
}

export interface JdExtraction {
  requirements: JdExtractedRequirement[];
  seniority: ExtractionSeniority;
  location?: string | null;
  /** The model's explicit "the JD is unclear about X" outlet — honesty, not filler. */
  ambiguities: string[];
}

export interface ShortlistResponse {
  requirements: string[];
  candidates: ShortlistCandidate[];
  extraction?: JdExtraction | null;
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
 * `degraded` + `notes` explain any partial results. `proposalId` (P1T-100) references the pending
 * approval record; absent when the run couldn't persist one. */
export interface StaffingReport {
  requirements: string[];
  candidates: StaffingReportCandidate[];
  recommendation?: { employeeId: string; narrative: string } | null;
  degraded: boolean;
  notes: string[];
  proposalId?: string | null;
  extraction?: JdExtraction | null;
}

/** A staffing proposal decision result (P1T-100). */
export interface StaffingProposalDecision {
  id: string;
  status: "pending" | "approved" | "rejected";
  decisionNote?: string | null;
}

/** Records the human decision on a staffing proposal — approve or reject, once. */
export async function decideStaffingProposal(
  proposalId: string,
  decision: "approved" | "rejected",
  note?: string,
): Promise<StaffingProposalDecision> {
  const res = await agentHttp.post<StaffingProposalDecision>(
    `/staffing/proposals/${proposalId}/decision`,
    { decision, note },
  );
  return res.data;
}

// ---- Approval inbox + drill-in (P1T-134/135) ----
// The inbox is the light index (snapshot columns); the drill-in carries the full persisted
// handoff package, so an approver decides from the package alone — nothing re-runs.

/** One candidate snapshot on an inbox row (deterministic, from the report at creation). */
export interface StaffingProposalCandidateSummary {
  employeeId: string;
  name: string;
  title: string;
  rank: number;
  matchScore?: number | null;
  matchBand?: string | null;
  rationale: string;
}

/** One inbox row (P1T-100 index). */
export interface StaffingProposalSummary {
  id: string;
  jobDescription: string;
  status: "pending" | "approved" | "rejected";
  createdAt: string;
  recommendedEmployeeId?: string | null;
  reportDegraded: boolean;
  candidates: StaffingProposalCandidateSummary[];
  decidedByUserId?: string | null;
  decidedAt?: string | null;
  decisionNote?: string | null;
}

/** One stage's slice of the run: which agent identity acted (client id + scopes — provenance,
 * never credentials), what model it used, what it cost, when, and how it ended. */
export interface HandoffStageSlice {
  stage: string;
  agentClientId?: string | null;
  scopes: string[];
  modelId?: string | null;
  inputTokens: number;
  outputTokens: number;
  startedAt: string;
  completedAt: string;
  status: "completed" | "failed" | "skipped";
  degradeReason?: string | null;
  retryCount?: number | null;
}

/** The persisted handoff package (P1T-133): everything the approver needs to trust the run. */
export interface HandoffPackage {
  inputs: Record<string, string | null>;
  report: StaffingReport;
  provenance: {
    callerUserId?: string | null;
    capsSnapshotAtStart: { window: string; used: number; cap: number; resetAt: string }[];
    startedAt: string;
  };
  slices: HandoffStageSlice[];
  degradations: { stage: string; whatWasLost: string; why: string }[];
}

/** The drill-in: inbox metadata + the package (null on rows older than the package column). */
export interface StaffingProposalDetail extends StaffingProposalSummary {
  package?: HandoffPackage | null;
}

/** The approval inbox index. */
export function useStaffingProposals(status: string = "pending") {
  return useQuery({
    queryKey: ["staffing-proposals", status],
    queryFn: async () =>
      (await agentHttp.get<StaffingProposalSummary[]>(`/staffing/proposals?status=${status}`)).data,
  });
}

/** The approver drill-in (P1T-134): metadata + the full handoff package. */
export async function getStaffingProposal(id: string): Promise<StaffingProposalDetail> {
  const res = await agentHttp.get<StaffingProposalDetail>(`/staffing/proposals/${id}`);
  return res.data;
}

// ---- Roster Scan (P1T-125) ----
// Exhaustive async scoring of the (filtered) roster against one JD: submit returns 202 with a job
// to poll — no SSE, jobs span hours and quota pauses; the job keeps running when the widget
// closes. Pausing (quota/cap windows) is the normal path, not an error.

export interface RosterScanRequest {
  jobDescription: string;
  availableOn?: string;
  skillIds?: string[];
  location?: string;
  minYears?: number;
}

export interface RosterScanEstimate {
  candidates: number;
  calls: number;
  rpdBudget: number;
}

export interface RosterScanAccepted {
  jobId: string;
  estimate: RosterScanEstimate;
}

export type RosterScanState = "queued" | "running" | "paused" | "completed" | "failed";
export type RosterScanCandidateStatus = "pending" | "scored" | "failed";

export interface RosterScanCandidate {
  employeeId: string;
  name: string;
  title: string;
  status: RosterScanCandidateStatus;
  score?: number | null;
  band?: string | null;
  rationale?: string | null;
  /** False with null score/band = the honest "the digest gave nothing to judge" outcome. */
  scorable?: boolean | null;
  error?: string | null;
}

export interface RosterScanProgress {
  scored: number;
  failed: number;
  pending: number;
  total: number;
  settled: number;
}

export interface RosterScanJob {
  jobId: string;
  state: RosterScanState;
  pauseReason?: "quota" | "cap" | null;
  resumeAt?: string | null;
  failureDetail?: string | null;
  createdAt: string;
  jobDescription: string;
  progress: RosterScanProgress;
  candidates: RosterScanCandidate[];
}

export function useSubmitRosterScan() {
  return useMutation({
    mutationFn: async (req: RosterScanRequest) =>
      (await agentHttp.post<RosterScanAccepted>("/roster-scan", req)).data,
  });
}

/** Polls one job while it is live; settles to no-refetch once terminal. */
export function useRosterScanJob(jobId: string | null) {
  return useQuery({
    queryKey: ["roster-scan", jobId],
    enabled: jobId !== null,
    queryFn: async () => (await agentHttp.get<RosterScanJob>(`/roster-scan/${jobId}`)).data,
    refetchInterval: (query) => {
      const state = query.state.data?.state;
      return state === "completed" || state === "failed" ? false : 3000;
    },
  });
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

/**
 * Server-side CV render (P1T-139). Fetched through axios rather than linked to directly so the
 * session token rides along on the request; the response is then handed to the browser as a
 * download under the filename the server chose.
 */
export function useDownloadCvPdf(id: string) {
  return useMutation({
    mutationFn: async () => {
      const res = await http.get<Blob>(`/employees/${id}/cv.pdf`, { responseType: "blob" });
      const disposition = (res.headers["content-disposition"] as string | undefined) ?? "";
      const filename = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? "cv.pdf";
      const url = URL.createObjectURL(res.data);
      try {
        const link = document.createElement("a");
        link.href = url;
        link.download = filename;
        link.click();
      } finally {
        URL.revokeObjectURL(url);
      }
    },
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
    mutationFn: async (dto: SaveEmployeeSkill) =>
      (await http.post<EmployeeSkill>(`/employees/${employeeId}/skills`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

/**
 * The level and the years, never the catalog link (P1T-156): `EmployeeSkillService.UpdateAsync`
 * validates `skillId` and then assigns only `Level` and `YearsExperience`. The id still rides along
 * so the payload is the one shape the API documents, and the form does not offer to change it.
 */
export function useUpdateEmployeeSkill(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveEmployeeSkill & { id: string }) =>
      (await http.put<EmployeeSkill>(`/employee-skills/${id}`, dto)).data,
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
    mutationFn: async (dto: SaveAvailabilityEntry) =>
      (await http.post<AvailabilityEntry>(`/employees/${employeeId}/availability`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useUpdateAvailability(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveAvailabilityEntry & { id: string }) =>
      (await http.put<AvailabilityEntry>(`/availability/${id}`, dto)).data,
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

// ---- Languages, qualifications, experiences (P1T-142) ----
//
// All three hang off one employee and are only ever read back through that employee's detail
// projection, so every mutation invalidates ["employees", employeeId] and nothing else — the
// same rule the skills and availability hooks above follow.

export function useAddLanguage(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveSpokenLanguage) =>
      (await http.post<SpokenLanguage>(`/employees/${employeeId}/languages`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useUpdateLanguage(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveSpokenLanguage & { id: string }) =>
      (await http.put<SpokenLanguage>(`/languages/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteLanguage(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/languages/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useAddQualification(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveQualification) =>
      (await http.post<Qualification>(`/employees/${employeeId}/qualifications`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useUpdateQualification(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveQualification & { id: string }) =>
      (await http.put<Qualification>(`/qualifications/${id}`, dto)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useDeleteQualification(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/qualifications/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["employees", employeeId] }),
  });
}

export function useAddExperience(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: SaveExperience) =>
      (await http.post<Experience>(`/employees/${employeeId}/experiences`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees", employeeId] });
      qc.invalidateQueries({ queryKey: ["employees", employeeId, "cv"] });
    },
  });
}

export function useUpdateExperience(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...dto }: SaveExperience & { id: string }) =>
      (await http.put<Experience>(`/experiences/${id}`, dto)).data,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees", employeeId] });
      qc.invalidateQueries({ queryKey: ["employees", employeeId, "cv"] });
    },
  });
}

export function useDeleteExperience(employeeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => http.delete(`/experiences/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["employees", employeeId] });
      qc.invalidateQueries({ queryKey: ["employees", employeeId, "cv"] });
    },
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
