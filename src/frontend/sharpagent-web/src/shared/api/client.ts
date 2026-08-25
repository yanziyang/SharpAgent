/**
 * Typed browser-to-API boundary. The browser only ever sends provider-neutral
 * identifiers; credentials and raw provider shapes never cross this file.
 */

export type HealthStatusValue = 'healthy' | 'degraded' | 'unready'

export type SessionMode = 'plan' | 'execute'
export type SessionStatus =
  | 'draft'
  | 'planning'
  | 'executing'
  | 'awaitingApproval'
  | 'reviewing'
  | 'completed'
  | 'failed'
  | 'cancelled'
  | 'interrupted'
export type RunStatus = Exclude<SessionStatus, 'draft'>

export interface HealthCheck {
  name: string
  status: HealthStatusValue
  detail: string | null
}

export interface HealthSnapshot {
  overall: HealthStatusValue
  checks: HealthCheck[]
  generatedAtUtc: string
}

export interface RunSummary {
  id: string
  sequence: number
  status: RunStatus
  startedAtUtc: string
  endedAtUtc: string | null
  stopReason: string | null
  resumeSourceRunId: string | null
  correlationId?: string
}

export interface Session {
  id: string
  workspaceId: string
  task: string
  mode: SessionMode
  status: SessionStatus
  modelProfileId: string
  policyProfileId: string
  activeRunId: string | null
  archived: boolean
  createdAtUtc: string
  updatedAtUtc: string
  runs: RunSummary[]
}

export interface SessionSummary {
  id: string
  task: string
  mode: SessionMode
  status: SessionStatus
  workspaceId: string
  modelProfileId: string
  activeRunId: string | null
  archived: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface DashboardStateCount {
  state: SessionStatus
  count: number
}

export interface DashboardSnapshot {
  periodDays: number
  sessionsByState: DashboardStateCount[]
  completedRuns: number
  averageDurationSeconds: number | null
  approvalCount: number
  toolFailureCount: number
  providerFailureCount: number
  contextCompactionCount: number
  estimatedCostUsd: number | null
  recentSessions: SessionSummary[]
}

export interface Workspace {
  id: string
  name: string
  rootPath: string
  status: string
  validationMessage: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ModelProfile {
  id: string
  provider: string
  displayName: string
  enabled: boolean
  validationStatus: string
  streaming: boolean
  toolCalling: boolean
  contextWindowTokens: number | null
  estimatedUsdPerMillionInputTokens: number | null
  estimatedUsdPerMillionOutputTokens: number | null
  eligibleForPlan: boolean
  eligibleForExecute: boolean
}

export interface PolicyProfile {
  id: string
  name: string
  maxRunDurationMinutes: number
  maxToolCalls: number
  maxEstimatedCostUsd: number
  approvalExpiryMinutes: number
}

export interface Approval {
  id: string
  runId: string
  sessionId: string
  actionType: string
  summary: string
  affectedPaths: string[]
  status: string
  expiresAtUtc: string
}

export interface ChangeSet {
  id: string
  runId: string
  status: string
  summary: string
  createdAtUtc: string
  files: Array<{
    path: string
    changeType: string
    binary: boolean
    diffPreview: string | null
  }>
}

export interface CreateSessionRequest {
  workspaceId: string
  task: string
  mode: SessionMode
  modelProfileId: string
  policyProfileId: string
}

export interface StartRunRequest {
  instruction?: string | null
  resumeFromRunId?: string | null
}

export interface StartRunResponse {
  session: Session
  run: RunSummary
}

export interface ResolveApprovalRequest {
  decision: 'approve_once' | 'deny' | 'cancel_run'
  comment?: string | null
}

/** Maps a failed API call to stable, user-safe problem information. */
export class ApiProblemError extends Error {
  readonly status: number

  /** Stable problem code from the API when present; UI maps it to copy. */
  readonly code: string | null

  constructor(status: number, message: string, code: string | null, options?: { cause?: unknown }) {
    super(message, options)
    this.name = 'ApiProblemError'
    this.status = status
    this.code = code
  }
}

function readStringField(body: unknown, field: string): string | null {
  if (typeof body === 'object' && body !== null && field in body) {
    const value = (body as Record<string, unknown>)[field]
    if (typeof value === 'string' && value.trim().length > 0) {
      return value
    }
  }

  return null
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(path, {
      ...init,
      headers: { Accept: 'application/json', ...init?.headers },
    })
  } catch (cause) {
    throw new ApiProblemError(0, 'The SharpAgent service is unreachable.', 'network_error', { cause })
  }

  if (!response.ok) {
    let problem: unknown = null
    try {
      problem = await response.json()
    } catch {
      problem = null
    }

    const message =
      readStringField(problem, 'detail') ?? readStringField(problem, 'title') ?? `Request failed (${response.status}).`

    throw new ApiProblemError(response.status, message, readStringField(problem, 'code'))
  }

  return (await response.json()) as T
}

export function createIdempotencyKey(operation: string): string {
  const suffix = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`
  return `${operation}-${suffix}`
}

export function apiCommand<T>(
  path: string,
  method: 'POST' | 'PATCH',
  body: unknown,
  operation: string,
  signal?: AbortSignal,
): Promise<T> {
  return apiFetch<T>(path, {
    method,
    signal,
    body: JSON.stringify(body),
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': createIdempotencyKey(operation),
    },
  })
}

export function fetchSessions(includeArchived = false, signal?: AbortSignal): Promise<SessionSummary[]> {
  const query = new URLSearchParams({ page: '1', pageSize: '50', includeArchived: String(includeArchived) })
  return apiFetch<SessionSummary[]>(`/api/sessions?${query.toString()}`, { signal })
}

export function fetchDashboard(periodDays = 30, signal?: AbortSignal): Promise<DashboardSnapshot> {
  const query = new URLSearchParams({ periodDays: String(periodDays) })
  return apiFetch<DashboardSnapshot>(`/api/dashboard?${query.toString()}`, { signal })
}

export function fetchSession(sessionId: string, signal?: AbortSignal): Promise<Session> {
  return apiFetch<Session>(`/api/sessions/${encodeURIComponent(sessionId)}`, { signal })
}

export function fetchWorkspaces(signal?: AbortSignal): Promise<Workspace[]> {
  return apiFetch<Workspace[]>('/api/workspaces', { signal })
}

export function fetchModelProfiles(signal?: AbortSignal): Promise<ModelProfile[]> {
  return apiFetch<ModelProfile[]>('/api/model-profiles', { signal })
}

export function fetchPolicyProfiles(signal?: AbortSignal): Promise<PolicyProfile[]> {
  return apiFetch<PolicyProfile[]>('/api/policy-profiles', { signal })
}

export function createSession(request: CreateSessionRequest, signal?: AbortSignal): Promise<Session> {
  return apiCommand<Session>('/api/sessions', 'POST', request, 'create-session', signal)
}

export function startRun(sessionId: string, request: StartRunRequest, signal?: AbortSignal): Promise<StartRunResponse> {
  return apiCommand<StartRunResponse>(
    `/api/sessions/${encodeURIComponent(sessionId)}/runs`,
    'POST',
    request,
    'start-run',
    signal,
  )
}

export function cancelRun(sessionId: string, signal?: AbortSignal): Promise<Session> {
  return apiCommand<Session>(
    `/api/sessions/${encodeURIComponent(sessionId)}/cancel`,
    'POST',
    {},
    'cancel-run',
    signal,
  )
}

export function archiveSession(sessionId: string, signal?: AbortSignal): Promise<Session> {
  return apiCommand<Session>(
    `/api/sessions/${encodeURIComponent(sessionId)}/archive`,
    'POST',
    {},
    'archive-session',
    signal,
  )
}

export function restoreSession(sessionId: string, signal?: AbortSignal): Promise<Session> {
  return apiCommand<Session>(
    `/api/sessions/${encodeURIComponent(sessionId)}/restore`,
    'POST',
    {},
    'restore-session',
    signal,
  )
}

export function fetchPendingApprovals(sessionId: string, signal?: AbortSignal): Promise<Approval[]> {
  return apiFetch<Approval[]>(`/api/sessions/${encodeURIComponent(sessionId)}/approvals/pending`, { signal })
}

export function resolveApproval(
  approvalId: string,
  request: ResolveApprovalRequest,
  signal?: AbortSignal,
): Promise<unknown> {
  return apiCommand<unknown>(
    `/api/approvals/${encodeURIComponent(approvalId)}/resolve`,
    'POST',
    request,
    'resolve-approval',
    signal,
  )
}

export function fetchChanges(sessionId: string, signal?: AbortSignal): Promise<ChangeSet[]> {
  return apiFetch<ChangeSet[]>(`/api/sessions/${encodeURIComponent(sessionId)}/changes`, { signal })
}

export function fetchHealthSnapshot(signal?: AbortSignal): Promise<HealthSnapshot> {
  return apiFetch<HealthSnapshot>('/api/health', { signal })
}
