import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import { statusLabel } from '@/features/sessions/session-types'
import type { SessionStatus } from '@/shared/api/client'

export function StatusBadge({ status, className }: { status: SessionStatus | string; className?: string }) {
  const variant = ['failed', 'cancelled'].includes(status) ? 'destructive' : status === 'completed' ? 'secondary' : 'outline'

  return (
    <Badge variant={variant} className={cn('status-badge', className)}>
      <span aria-hidden className={cn('status-dot', `status-dot-${status}`)} />
      {statusLabel(status as SessionStatus)}
    </Badge>
  )
}
