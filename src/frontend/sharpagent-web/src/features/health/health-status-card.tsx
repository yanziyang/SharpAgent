import { Badge } from '@/components/ui/badge'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { HEALTH_BADGE_VARIANTS, healthStatusLabel } from './health-status-labels'

export interface HealthStatusCardProps {
  snapshot: {
    overall: 'healthy' | 'degraded' | 'unready'
    checks: { name: string; status: 'healthy' | 'degraded' | 'unready'; detail: string | null }[]
  }
}

export function HealthStatusCard({ snapshot }: HealthStatusCardProps) {
  return (
    <Card aria-label="Service health">
      <CardHeader>
        <div className="flex items-center justify-between gap-2">
          <CardTitle>Service health</CardTitle>
          <Badge variant={HEALTH_BADGE_VARIANTS[snapshot.overall]}>
            {healthStatusLabel(snapshot.overall)}
          </Badge>
        </div>
        <CardDescription>Readiness of the local SharpAgent backend and its dependencies.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <ul className="flex flex-col gap-2">
          {snapshot.checks.map((check) => (
            <li key={check.name} className="flex items-center justify-between gap-3 text-sm">
              <span className="font-medium">{check.name}</span>
              <span className="flex items-center gap-2">
                {check.detail ? <span className="text-muted-foreground">{check.detail}</span> : null}
                <Badge variant={HEALTH_BADGE_VARIANTS[check.status]}>{healthStatusLabel(check.status)}</Badge>
              </span>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}
