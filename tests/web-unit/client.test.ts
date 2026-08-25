import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  ApiProblemError,
  apiFetch,
  createIdempotencyKey,
  createSession,
  fetchChanges,
  fetchDashboard,
  fetchHealthSnapshot,
  fetchModelProfiles,
  fetchPendingApprovals,
  fetchPolicyProfiles,
  fetchSession,
  fetchSessions,
  fetchWorkspaces,
  resolveApproval,
  restoreSession,
  archiveSession,
  cancelRun,
  startRun,
} from '@/shared/api/client'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('apiFetch', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('returns parsed JSON on success and sends Accept header', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await apiFetch<{ ok: boolean }>('/api/example')

    expect(result).toEqual({ ok: true })
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/example',
      expect.objectContaining({ headers: expect.objectContaining({ Accept: 'application/json' }) }),
    )
  })

  it('maps problem details to ApiProblemError with status and code', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(409, { title: 'Conflict', detail: 'Session has an active run.', code: 'session_active_run' })),
    )

    const error = await apiFetch('/api/example').catch((cause: unknown) => cause)

    expect(error).toBeInstanceOf(ApiProblemError)
    const problem = error as ApiProblemError
    expect(problem.status).toBe(409)
    expect(problem.code).toBe('session_active_run')
    expect(problem.message).toBe('Session has an active run.')
  })

  it('falls back to a generic message when the failure body is not JSON', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('<html/>', { status: 502 })),
    )

    const error = await apiFetch('/api/example').catch((cause: unknown) => cause)

    expect((error as ApiProblemError).status).toBe(502)
    expect((error as ApiProblemError).code).toBeNull()
    expect((error as ApiProblemError).message).toContain('502')
  })

  it('treats blank problem fields as absent', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(jsonResponse(400, { detail: '   ', title: '', code: '' })),
    )

    const error = await apiFetch('/api/example').catch((cause: unknown) => cause)

    expect((error as ApiProblemError).code).toBeNull()
    expect((error as ApiProblemError).message).toContain('400')
  })

  it('maps network failures to an unreachable-service problem', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fetch failed')))

    const error = await apiFetch('/api/example').catch((cause: unknown) => cause)

    expect((error as ApiProblemError).status).toBe(0)
    expect((error as ApiProblemError).code).toBe('network_error')
  })
})

describe('fetchHealthSnapshot', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('parses the health contract and forwards abort signals', async () => {
    const snapshot = {
      overall: 'degraded',
      checks: [{ name: 'database', status: 'degraded', detail: null }],
      generatedAtUtc: '2026-08-23T10:00:00Z',
    }
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, snapshot))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const result = await fetchHealthSnapshot(controller.signal)

    expect(result.overall).toBe('degraded')
    expect(result.checks[0]?.name).toBe('database')
    expect(fetchMock).toHaveBeenCalledWith('/api/health', expect.objectContaining({ signal: controller.signal }))
  })
})

describe('fetchDashboard', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads the server-authoritative dashboard projection', async () => {
    const snapshot = {
      periodDays: 30,
      sessionsByState: [{ state: 'completed', count: 2 }],
      completedRuns: 2,
      averageDurationSeconds: 18.5,
      approvalCount: 1,
      toolFailureCount: 0,
      providerFailureCount: 0,
      contextCompactionCount: 1,
      estimatedCostUsd: 0.42,
      recentSessions: [],
    }
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, snapshot))
    vi.stubGlobal('fetch', fetchMock)

    await expect(fetchDashboard()).resolves.toEqual(snapshot)
    expect(fetchMock).toHaveBeenCalledWith('/api/dashboard?periodDays=30', expect.objectContaining({ headers: { Accept: 'application/json' } }))
  })
})

describe('session and catalog client operations', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds encoded query and resource paths', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(200, [])))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    await fetchSessions(true, controller.signal)
    await fetchSession('ses/with space', controller.signal)
    await fetchWorkspaces(controller.signal)
    await fetchModelProfiles(controller.signal)
    await fetchPolicyProfiles(controller.signal)
    await fetchPendingApprovals('ses/with space', controller.signal)
    await fetchChanges('ses/with space', controller.signal)

    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/sessions?page=1&pageSize=50&includeArchived=true',
      '/api/sessions/ses%2Fwith%20space',
      '/api/workspaces',
      '/api/model-profiles',
      '/api/policy-profiles',
      '/api/sessions/ses%2Fwith%20space/approvals/pending',
      '/api/sessions/ses%2Fwith%20space/changes',
    ])
    expect((fetchMock.mock.calls[0]?.[1] as RequestInit).signal).toBe(controller.signal)
  })

  it('sends idempotent state-changing commands with provider-neutral JSON', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(200, {})))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      workspaceId: 'ws_1',
      task: 'Inspect the parser',
      mode: 'plan' as const,
      modelProfileId: 'model_1',
      policyProfileId: 'policy_1',
    }

    expect(createIdempotencyKey('test-operation')).toMatch(/^test-operation-/)
    await createSession(request)
    await startRun('ses/1', { instruction: 'continue', resumeFromRunId: null })
    await cancelRun('ses/1')
    await archiveSession('ses/1')
    await restoreSession('ses/1')
    await resolveApproval('approval/1', { decision: 'deny', comment: 'Too broad' })

    expect(fetchMock).toHaveBeenCalledTimes(6)
    const [createPath, createInit] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(createPath).toBe('/api/sessions')
    expect(createInit.method).toBe('POST')
    expect(createInit.body).toBe(JSON.stringify(request))
    expect(createInit.headers).toEqual(expect.objectContaining({
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'Idempotency-Key': expect.stringMatching(/^create-session-/),
    }))
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/sessions',
      '/api/sessions/ses%2F1/runs',
      '/api/sessions/ses%2F1/cancel',
      '/api/sessions/ses%2F1/archive',
      '/api/sessions/ses%2F1/restore',
      '/api/approvals/approval%2F1/resolve',
    ])
  })
})
