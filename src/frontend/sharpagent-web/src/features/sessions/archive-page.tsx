import { useState } from 'react'
import { Link } from 'react-router'
import { Archive, ArrowRight, RotateCcw } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty'
import { PageFrame, PageHeader, ErrorState, LoadingState } from '@/components/page-state'
import { StatusBadge } from '@/components/status-badge'
import { useSessionList } from '@/features/sessions/use-session-data'
import { restoreSession } from '@/shared/api/client'

export function ArchivePage() {
  const sessions = useSessionList(true)
  const archived = sessions.kind === 'ready' && Array.isArray(sessions.data) ? sessions.data.filter((session) => session.archived) : []
  const [restoring, setRestoring] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const restore = async (sessionId: string) => {
    setRestoring(sessionId)
    setError(null)
    try {
      await restoreSession(sessionId)
      sessions.reload()
    } catch (cause: unknown) {
      setError(cause instanceof Error ? cause.message : 'The session could not be restored.')
    } finally {
      setRestoring(null)
    }
  }

  return <PageFrame><PageHeader eyebrow="History" title="Archived sessions" description="Inactive sessions stay recoverable without deleting audit history, changes, or results." actions={<Button variant="ghost" render={<Link to="/" />}>Back to dashboard <ArrowRight data-icon="inline-end" /></Button>} />
    {error ? <Alert variant="destructive"><AlertTitle>Restore failed</AlertTitle><AlertDescription>{error}</AlertDescription></Alert> : null}
    {sessions.kind === 'loading' ? <LoadingState label="Loading archived sessions…" /> : null}
    {sessions.kind === 'error' ? <ErrorState message={sessions.message} onRetry={sessions.reload} /> : null}
    {sessions.kind === 'ready' && archived.length === 0 ? <Empty className="empty-panel"><EmptyHeader><Archive /><EmptyTitle>No archived sessions</EmptyTitle><EmptyDescription>Archive an inactive session from its run controls when you want to keep the active workspace focused.</EmptyDescription></EmptyHeader></Empty> : null}
    {archived.length > 0 ? <div className="archive-list">{archived.map((session) => <Card key={session.id} size="sm"><CardContent className="archive-row"><div className="session-summary-main"><StatusBadge status={session.status} /><h2>{session.task}</h2><p>{session.workspaceId} · archived {new Date(session.updatedAtUtc).toLocaleString()}</p></div><Button variant="outline" size="sm" onClick={() => void restore(session.id)} disabled={restoring !== null}><RotateCcw data-icon="inline-start" />{restoring === session.id ? 'Restoring…' : 'Restore'}</Button></CardContent></Card>)}</div> : null}
  </PageFrame>
}
