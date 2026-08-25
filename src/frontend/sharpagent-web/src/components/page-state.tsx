import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: string
  title: string
  description?: string
  actions?: React.ReactNode
}) {
  return (
    <header className="page-header">
      <div className="page-heading">
        {eyebrow ? <p className="page-eyebrow">{eyebrow}</p> : null}
        <h1>{title}</h1>
        {description ? <p className="page-description">{description}</p> : null}
      </div>
      {actions ? <div className="page-actions">{actions}</div> : null}
    </header>
  )
}

export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return <p role="status" className="page-loading">{label}</p>
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <Alert variant="destructive" className="page-error">
      <AlertTitle>Unable to load this view</AlertTitle>
      <AlertDescription className="flex items-center justify-between gap-3">
        <span>{message}</span>
        {onRetry ? <Button variant="outline" size="sm" onClick={onRetry}>Retry</Button> : null}
      </AlertDescription>
    </Alert>
  )
}

export function PageFrame({ children, className }: { children: React.ReactNode; className?: string }) {
  return <div className={cn('page-frame', className)}>{children}</div>
}
