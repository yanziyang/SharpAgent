import { BarChart3, Clock3, DollarSign, ShieldCheck, Wrench } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ErrorState, LoadingState, PageFrame, PageHeader } from '@/components/page-state'
import type { DashboardSnapshot } from '@/shared/api/client'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { useDashboard } from './use-dashboard'

const periodOptions = [7, 30, 90] as const
type PeriodDays = (typeof periodOptions)[number]

const stateLabels: Record<string, string> = {
  draft: 'Draft',
  planning: 'Planning',
  executing: 'Executing',
  awaitingApproval: 'Awaiting approval',
  reviewing: 'Reviewing',
  completed: 'Completed',
  failed: 'Failed',
  cancelled: 'Cancelled',
  interrupted: 'Interrupted',
}

export function StatisticsPage() {
  const [periodDays, setPeriodDays] = useState<PeriodDays>(30)
  const dashboard = useDashboard(periodDays)

  return (
    <PageFrame>
      <PageHeader
        eyebrow="Reports"
        title="Statistics"
        description="Persisted run, approval, tool, and usage metrics for the trusted-local workspace."
        actions={
          <div className="report-controls" role="group" aria-label="Statistics period">
            {periodOptions.map((days) => (
              <Button
                key={days}
                size="sm"
                variant={periodDays === days ? 'secondary' : 'ghost'}
                aria-pressed={periodDays === days}
                onClick={() => setPeriodDays(days)}
              >
                {days} days
              </Button>
            ))}
          </div>
        }
      />
      {dashboard.kind === 'loading' ? <LoadingState label="Loading statistics…" /> : null}
      {dashboard.kind === 'error' ? <ErrorState message={dashboard.message} onRetry={dashboard.reload} /> : null}
      {dashboard.kind === 'ready' ? <StatisticsContent snapshot={dashboard.data} /> : null}
    </PageFrame>
  )
}

function StatisticsContent({ snapshot }: { snapshot: DashboardSnapshot }) {
  const averageDuration = snapshot.averageDurationSeconds === null
    ? '—'
    : `${Math.round(snapshot.averageDurationSeconds)}s`
  const estimatedCost = snapshot.estimatedCostUsd === null
    ? '—'
    : `$${snapshot.estimatedCostUsd.toFixed(2)}`

  return (
    <>
      <section className="metric-grid statistics-metric-grid" aria-label="Persisted run metrics">
        <MetricCard icon={<ShieldCheck />} label="Completed runs" value={String(snapshot.completedRuns)} detail="Durable results retained" />
        <MetricCard icon={<Clock3 />} label="Average duration" value={averageDuration} detail="Completed runs with timing" />
        <MetricCard icon={<BarChart3 />} label="Approvals" value={String(snapshot.approvalCount)} detail="Recorded approval requests" />
        <MetricCard icon={<DollarSign />} label="Estimated cost" value={estimatedCost} detail="Provider usage estimates" />
        <MetricCard icon={<Wrench />} label="Tool failures" value={String(snapshot.toolFailureCount)} detail="Bounded execution failures" />
        <MetricCard icon={<Wrench />} label="Provider failures" value={String(snapshot.providerFailureCount)} detail="Sanitized run failures" />
        <MetricCard icon={<BarChart3 />} label="Compactions" value={String(snapshot.contextCompactionCount)} detail="Context recovery events" />
      </section>

      <div className="statistics-grid">
        <Card>
          <CardHeader>
            <CardTitle>Sessions by state</CardTitle>
            <CardDescription>Active and retained sessions projected by the server.</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="statistics-state-list">
              {snapshot.sessionsByState.length === 0 ? <p className="detail-muted">No sessions have been recorded yet.</p> : null}
              {snapshot.sessionsByState.map((entry) => (
                <div className="statistics-state-row" key={entry.state}>
                  <span>{stateLabels[entry.state] ?? entry.state}</span>
                  <Badge variant="outline">{entry.count}</Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Recent activity</CardTitle>
            <CardDescription>Latest session projections; no raw provider payloads are shown.</CardDescription>
          </CardHeader>
          <CardContent>
            {snapshot.recentSessions.length === 0 ? <p className="detail-muted">No recent sessions.</p> : null}
            <div className="statistics-recent-list">
              {snapshot.recentSessions.slice(0, 8).map((session) => (
                <div className="statistics-recent-row" key={session.id}>
                  <div><strong>{session.task}</strong><span>{session.workspaceId} · {stateLabels[session.status] ?? session.status}</span></div>
                  <time dateTime={session.updatedAtUtc}>{new Date(session.updatedAtUtc).toLocaleDateString()}</time>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>
    </>
  )
}

function MetricCard({ icon, label, value, detail }: { icon: React.ReactNode; label: string; value: string; detail: string }) {
  return <Card size="sm"><CardHeader><CardDescription>{label}</CardDescription><CardTitle>{value}</CardTitle></CardHeader><CardContent className="metric-caption">{icon}{detail}</CardContent></Card>
}
