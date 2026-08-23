import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useHealth } from '@/features/health/use-health'

const healthySnapshot = {
  overall: 'healthy',
  checks: [],
  generatedAtUtc: '2026-08-23T10:00:00Z',
}

describe('useHealth', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('loads the snapshot into state', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(JSON.stringify(healthySnapshot), { status: 200 })),
    )

    const { result } = renderHook(() => useHealth())

    await waitFor(() => {
      expect(result.current.kind).toBe('ok')
    })
    expect(result.current).toMatchObject({ kind: 'ok' })
  })

  it('surfaces the wrapped client message for network failures', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fetch failed')))

    const { result } = renderHook(() => useHealth())

    await waitFor(() => {
      expect(result.current.kind).toBe('error')
    })
    expect(result.current.kind === 'error' && result.current.message).toBe(
      'The SharpAgent service is unreachable.',
    )
  })

  it('ignores responses arriving after unmount', async () => {
    let resolve!: (response: Response) => void
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(() => new Promise<Response>((res) => (resolve = res))),
    )

    const { result, unmount } = renderHook(() => useHealth())
    unmount()

    resolve(new Response(JSON.stringify(healthySnapshot), { status: 200 }))
    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.kind).toBe('loading')
  })
})
