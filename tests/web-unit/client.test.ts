import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiProblemError, apiFetch, fetchHealthSnapshot } from '@/shared/api/client'

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
