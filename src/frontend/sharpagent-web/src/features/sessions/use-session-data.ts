import { useCallback } from 'react'
import {
  fetchPendingApprovals,
  fetchSession,
  fetchSessions,
  type Approval,
  type Session,
  type SessionSummary,
} from '@/shared/api/client'
import { useResource, type ResourceState } from '@/shared/api/use-resource'

export function useSession(sessionId: string | undefined): ResourceState<Session> & { reload: () => void } {
  const loader = useCallback(
    (signal: AbortSignal) => {
      if (!sessionId) {
        return Promise.reject(new Error('A session identifier is required.'))
      }
      return fetchSession(sessionId, signal)
    },
    [sessionId],
  )

  return useResource(`session:${sessionId ?? 'missing'}`, loader)
}

export function useSessionList(includeArchived = false, enabled = true): ResourceState<SessionSummary[]> & { reload: () => void } {
  const loader = useCallback(
    (signal: AbortSignal) => enabled ? fetchSessions(includeArchived, signal) : Promise.resolve([]),
    [enabled, includeArchived],
  )

  return useResource(`sessions:${includeArchived}:${enabled}`, loader)
}

export function usePendingApprovals(
  sessionId: string | undefined,
): ResourceState<Approval[]> & { reload: () => void } {
  const loader = useCallback(
    (signal: AbortSignal) => {
      if (!sessionId) {
        return Promise.resolve([])
      }
      return fetchPendingApprovals(sessionId, signal)
    },
    [sessionId],
  )

  return useResource(`approvals:${sessionId ?? 'missing'}`, loader)
}
