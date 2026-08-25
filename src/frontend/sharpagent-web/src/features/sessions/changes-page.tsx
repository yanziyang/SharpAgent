import { useCallback } from 'react'
import { Link, useParams } from 'react-router'
import { ArrowLeft, FileCode2, FileText } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { PageFrame, PageHeader, ErrorState, LoadingState } from '@/components/page-state'
import { useResource } from '@/shared/api/use-resource'
import { fetchChanges, type ChangeSet } from '@/shared/api/client'

export function ChangesPage() {
  const { sessionId } = useParams()
  const loader = useCallback((signal: AbortSignal) => sessionId ? fetchChanges(sessionId, signal) : Promise.reject(new Error('A session identifier is required.')), [sessionId])
  const changes = useResource(`changes:${sessionId ?? 'missing'}`, loader)

  return <PageFrame><PageHeader eyebrow="Review" title="Changes" description="Server-recorded change evidence, bounded to the selected run worktree." actions={<Button variant="ghost" render={<Link to={sessionId ? `/sessions/${sessionId}` : '/'} />}><ArrowLeft data-icon="inline-start" />Back to session</Button>} />
    {changes.kind === 'loading' ? <LoadingState label="Loading change evidence…" /> : null}
    {changes.kind === 'error' ? <ErrorState message={changes.message} onRetry={changes.reload} /> : null}
    {changes.kind === 'ready' && changes.data.length === 0 ? <Card><CardHeader><CardTitle>No changes recorded</CardTitle><CardDescription>The current session has not produced a change set.</CardDescription></CardHeader></Card> : null}
    {changes.kind === 'ready' ? <div className="changes-list">{changes.data.map((change: ChangeSet) => <Card key={change.id}><CardHeader><div className="change-heading"><FileCode2 aria-hidden /><div><CardTitle>{change.summary}</CardTitle><CardDescription>{change.status} · {new Date(change.createdAtUtc).toLocaleString()}</CardDescription></div></div></CardHeader><CardContent><div className="change-file-list">{change.files.map((file) => <details key={file.path} className="change-file"><summary><FileText aria-hidden /><code>{file.path}</code><span>{file.changeType}</span></summary><pre>{file.diffPreview ?? 'No preview supplied.'}</pre></details>)}</div></CardContent></Card>)}</div> : null}
  </PageFrame>
}
