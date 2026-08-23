import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import { HealthStatusCard } from '@/features/health/health-status-card'
import { useHealth } from '@/features/health/use-health'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'

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
  return (
    <div className="flex flex-col gap-6">
      <section aria-labelledby="dashboard-heading" className="flex flex-col gap-1">
        <h1 id="dashboard-heading" className="text-xl font-semibold tracking-tight">
          Dashboard
        </h1>
        <p className="text-sm text-muted-foreground">
          Recent coding-agent sessions and current service status.
        </p>
      </section>

      <HealthSection />

      <Empty>
        <EmptyHeader>
          <EmptyTitle>No sessions yet</EmptyTitle>
          <EmptyDescription>
            Create your first task to plan or execute repository work with a controlled agent run.
          </EmptyDescription>
        </EmptyHeader>
      </Empty>
    </div>
  )
}
