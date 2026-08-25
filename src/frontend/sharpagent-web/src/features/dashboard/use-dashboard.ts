import { useCallback } from 'react'
import { fetchDashboard, type DashboardSnapshot } from '@/shared/api/client'
import { useResource, type ResourceState } from '@/shared/api/use-resource'

export function useDashboard(periodDays = 30): ResourceState<DashboardSnapshot> & { reload: () => void } {
  const loader = useCallback((signal: AbortSignal) => fetchDashboard(periodDays, signal), [periodDays])
  return useResource(`dashboard:${periodDays}`, loader)
}
