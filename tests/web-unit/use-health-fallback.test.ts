import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useHealth } from '@/features/health/use-health'

// Isolate the hook from the API client to prove its defensive fallback path.
vi.mock('@/shared/api/client', () => ({
  fetchHealthSnapshot: () => Promise.reject('not-an-error-object'),
}))

describe('useHealth with a hostile rejection value', () => {
  it('reports a safe fallback message for non-error rejections', async () => {
    const { result } = renderHook(() => useHealth())

    await waitFor(() => {
      expect(result.current.kind).toBe('error')
    })

    expect(result.current.kind === 'error' && result.current.message).toBe('Unable to load health.')
  })
})
