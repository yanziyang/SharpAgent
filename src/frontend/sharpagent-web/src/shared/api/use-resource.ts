import { useCallback, useEffect, useState } from 'react'

export type ResourceState<T> =
  | { kind: 'loading'; data: T | null }
  | { kind: 'ready'; data: T }
  | { kind: 'error'; data: T | null; message: string }

interface InternalResourceState<T> {
  requestKey: string
  value: ResourceState<T>
}

export function useResource<T>(
  key: string,
  loader: (signal: AbortSignal) => Promise<T>,
): ResourceState<T> & { reload: () => void } {
  const [reloadToken, setReloadToken] = useState(0)
  const requestKey = `${key}:${reloadToken}`
  const [state, setState] = useState<InternalResourceState<T>>({
    requestKey,
    value: { kind: 'loading', data: null },
  })

  const reload = useCallback(() => setReloadToken((value) => value + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    loader(controller.signal)
      .then((data) => {
        if (active) {
          setState({ requestKey, value: { kind: 'ready', data } })
        }
      })
      .catch((cause: unknown) => {
        if (active && !controller.signal.aborted) {
          setState({
            requestKey,
            value: {
              kind: 'error',
              data: null,
              message: cause instanceof Error ? cause.message : 'The service returned an unusable response.',
            },
          })
        }
      })

    return () => {
      active = false
      controller.abort()
    }
  }, [loader, requestKey])

  const visibleState = state.requestKey === requestKey
    ? state.value
    : { kind: 'loading' as const, data: state.value.data }

  return { ...visibleState, reload }
}
