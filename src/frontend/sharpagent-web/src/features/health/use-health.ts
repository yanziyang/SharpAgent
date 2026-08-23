import { useEffect, useState } from 'react'
import { fetchHealthSnapshot, type HealthSnapshot } from '@/shared/api/client'

export type HealthQueryState =
  | { kind: 'loading' }
  | { kind: 'ok'; snapshot: HealthSnapshot }
  | { kind: 'error'; message: string }

/** Loads the service health projection; server state is authoritative. */
export function useHealth(): HealthQueryState {
  const [state, setState] = useState<HealthQueryState>({ kind: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    let cancelled = false

    fetchHealthSnapshot(controller.signal)
      .then((snapshot) => {
        if (!cancelled) {
          setState({ kind: 'ok', snapshot })
        }
      })
      .catch((cause: unknown) => {
        if (!cancelled) {
          setState({
            kind: 'error',
            message: cause instanceof Error ? cause.message : 'Unable to load health.',
          })
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [])

  return state
}
