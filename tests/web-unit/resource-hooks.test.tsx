import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useCatalog } from '@/features/catalog/use-catalog'
import { usePendingApprovals, useSession, useSessionList } from '@/features/sessions/use-session-data'
import { useResource } from '@/shared/api/use-resource'

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

describe('resource hooks', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('tracks loading, ready, reload, and key changes without stale results', async () => {
    let resolveRequest!: (value: string) => void
    const loader = vi.fn(() => new Promise<string>((resolve) => {
      resolveRequest = resolve
    }))
    const { result, rerender } = renderHook(({ resourceKey }) => useResource(resourceKey, loader), {
      initialProps: { resourceKey: 'first' },
    })

    expect(result.current.kind).toBe('loading')
    resolveRequest('first result')
    await waitFor(() => expect(result.current).toMatchObject({ kind: 'ready', data: 'first result' }))

    act(() => result.current.reload())
    expect(result.current).toMatchObject({ kind: 'loading', data: 'first result' })
    resolveRequest('reloaded result')
    await waitFor(() => expect(result.current).toMatchObject({ kind: 'ready', data: 'reloaded result' }))

    act(() => rerender({ resourceKey: 'second' }))
    expect(result.current.kind).toBe('loading')
    resolveRequest('second result')
    await waitFor(() => expect(result.current).toMatchObject({ kind: 'ready', data: 'second result' }))
    expect(loader).toHaveBeenCalledTimes(3)
  })

  it('surfaces safe loader errors and ignores an aborted request', async () => {
    const loader = vi.fn().mockRejectedValue(new Error('catalog unavailable'))
    const { result } = renderHook(() => useResource('error', loader))
    await waitFor(() => expect(result.current.kind).toBe('error'))
    expect(result.current).toMatchObject({ message: 'catalog unavailable', data: null })

    let resolveAborted!: (value: string) => void
    const pending = renderHook(() => useResource('aborted', () => new Promise<string>((resolve) => {
      resolveAborted = resolve
    })))
    pending.unmount()
    resolveAborted('late result')

    const hostile = renderHook(() => useResource('hostile', () => Promise.reject('not an Error')))
    await waitFor(() => expect(hostile.result.current.kind).toBe('error'))
    expect(hostile.result.current).toMatchObject({ message: 'The service returned an unusable response.' })
  })
})

describe('catalog and session data hooks', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads the three catalog resources and supports disabled session lists', async () => {
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL) => {
      const path = String(input)
      if (path.includes('/workspaces')) return Promise.resolve(jsonResponse([{ id: 'ws_1' }]))
      if (path.includes('/model-profiles')) return Promise.resolve(jsonResponse([{ id: 'model_1' }]))
      return Promise.resolve(jsonResponse([{ id: 'policy_1' }]))
    })
    vi.stubGlobal('fetch', fetchMock)

    const catalog = renderHook(() => useCatalog())
    await waitFor(() => expect(catalog.result.current.kind).toBe('ready'))
    expect(catalog.result.current.data).toEqual({
      workspaces: [{ id: 'ws_1' }],
      modelProfiles: [{ id: 'model_1' }],
      policyProfiles: [{ id: 'policy_1' }],
    })

    const disabled = renderHook(() => useSessionList(false, false))
    await waitFor(() => expect(disabled.result.current.kind).toBe('ready'))
    expect(disabled.result.current.data).toEqual([])
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('handles missing and present session identifiers', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    const missingSession = renderHook(() => useSession(undefined))
    await waitFor(() => expect(missingSession.result.current.kind).toBe('error'))
    expect(missingSession.result.current).toMatchObject({ message: 'A session identifier is required.' })

    const missingApprovals = renderHook(() => usePendingApprovals(undefined))
    await waitFor(() => expect(missingApprovals.result.current.kind).toBe('ready'))
    expect(missingApprovals.result.current.data).toEqual([])

    const approvals = renderHook(() => usePendingApprovals('ses_1'))
    await waitFor(() => expect(approvals.result.current.kind).toBe('ready'))
    expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/approvals/pending', expect.anything())
  })
})
