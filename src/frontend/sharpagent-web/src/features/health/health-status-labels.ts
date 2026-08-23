import type { HealthStatusValue } from '@/shared/api/client'

/** Non-color-only labels; every status also reads as text for accessibility. */
export const HEALTH_STATUS_LABELS: Record<HealthStatusValue, string> = {
  healthy: 'Healthy',
  degraded: 'Degraded',
  unready: 'Unready',
}

export function healthStatusLabel(status: HealthStatusValue): string {
  return HEALTH_STATUS_LABELS[status]
}

export const HEALTH_BADGE_VARIANTS: Record<HealthStatusValue, 'secondary' | 'outline' | 'destructive'> = {
  healthy: 'secondary',
  degraded: 'outline',
  unready: 'destructive',
}
