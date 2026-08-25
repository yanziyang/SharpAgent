import { Link } from 'react-router'
import { Activity, ArrowRight, CirclePlus, ShieldCheck } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import { HealthStatusCard } from '@/features/health/health-status-card'
import { useHealth } from '@/features/health/use-health'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { PageFrame, PageHeader, LoadingState, ErrorState } from '@/components/page-state'
import { StatusBadge } from '@/components/status-badge'
import { statusLabel } from '@/features/sessions/session-types'
import { useDashboard } from './use-dashboard'

function HealthSection() {
  const state = useHealth()

  if (state.kind === 'loading') {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Loading service health…
      </p>
    )
  }

  if (state.kind === 'error') {
    return (
      <Alert>
        <AlertTitle>Health unavailable</AlertTitle>
        <AlertDescription>{state.message}</AlertDescription>
      </Alert>
    )
  }

  return <HealthStatusCard snapshot={state.snapshot} />
}

export function DashboardPage() {
  const dashboard = useDashboard(30)
  const snapshot = dashboard.kind === 'ready' ? dashboard.data : null
  const recentSessions = snapshot && Array.isArray(snapshot.recentSessions) ? snapshot.recentSessions : []
  const stateCounts = snapshot && Array.isArray(snapshot.sessionsByState) ? snapshot.sessionsByState : []
  const countStates = (states: string[]) => stateCounts
    .filter((entry) => states.includes(entry.state))
    .reduce((total, entry) => total + entry.count, 0) ?? 0
  const activeCount = countStates(['planning', 'executing', 'awaitingApproval', 'reviewing'])
  const attentionCount = countStates(['awaitingApproval', 'interrupted', 'failed'])

  return (
    <PageFrame>
      <PageHeader
        eyebrow="Workspace overview"
        title="Dashboard"
        description="Recent coding-agent sessions and current service status."
        actions={<Button render={<Link to="/sessions/new" />}><CirclePlus data-icon="inline-start" />New session</Button>}
      />

      <section className="metric-grid" aria-label="Session summary">
        <Card size="sm"><CardHeader><CardDescription>Active sessions</CardDescription><CardTitle>{activeCount}</CardTitle></CardHeader><CardContent className="metric-caption"><Activity /> Live or awaiting a controlled action</CardContent></Card>
        <Card size="sm"><CardHeader><CardDescription>Completed runs</CardDescription><CardTitle>{snapshot?.completedRuns ?? 0}</CardTitle></CardHeader><CardContent className="metric-caption"><ShieldCheck /> Durable results retained</CardContent></Card>
        <Card size="sm"><CardHeader><CardDescription>Needs attention</CardDescription><CardTitle>{attentionCount}</CardTitle></CardHeader><CardContent className="metric-caption">Approval, recovery, or bounded failure</CardContent></Card>
        <Card size="sm"><CardHeader><CardDescription>Deployment</CardDescription><CardTitle>Trusted local</CardTitle></CardHeader><CardContent className="metric-caption">No authentication by design</CardContent></Card>
      </section>

      {dashboard.kind === 'error' ? <ErrorState message={dashboard.message} onRetry={dashboard.reload} /> : null}

      <HealthSection />

      <section className="dashboard-sessions" aria-labelledby="recent-sessions-heading">
        <div className="section-heading-row">
          <div><p className="page-eyebrow">Activity</p><h2 id="recent-sessions-heading">Recent sessions</h2></div>
          <Button variant="ghost" size="sm" render={<Link to="/sessions/archive" />}>View archive <ArrowRight data-icon="inline-end" /></Button>
        </div>
        {dashboard.kind === 'loading' ? <LoadingState label="Loading recent sessions…" /> : null}
        {dashboard.kind === 'ready' && recentSessions.length === 0 ? (
          <Empty className="empty-panel">
            <EmptyHeader>
              <EmptyTitle>No sessions yet</EmptyTitle>
              <EmptyDescription>Create your first task to plan or execute repository work with a controlled agent run.</EmptyDescription>
            </EmptyHeader>
            <EmptyContent><Button render={<Link to="/sessions/new" />}>Create a session</Button></EmptyContent>
          </Empty>
        ) : null}
        {recentSessions.length > 0 ? <div className="session-card-list">{recentSessions.slice(0, 8).map((session) => (
          <Link key={session.id} to={`/sessions/${session.id}`} className="session-summary-card">
            <div className="session-summary-main"><StatusBadge status={session.status} /><h3>{session.task}</h3><p>{session.workspaceId} · {session.mode === 'plan' ? 'Plan only' : 'Controlled execute'}</p></div>
            <div className="session-summary-meta"><Badge variant="outline">{statusLabel(session.status)}</Badge><span>{new Date(session.updatedAtUtc).toLocaleString()}</span><ArrowRight aria-hidden /></div>
          </Link>
        ))}</div> : null}
      </section>
    </PageFrame>
  )
}
