import { useCallback, useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import {
  Check,
  ChevronRight,
  Clock3,
  Code2,
  FileText,
  FolderSearch,
  Layers3,
  Paperclip,
  Play,
  RefreshCw,
  Send,
  ShieldCheck,
  Sparkles,
  TerminalSquare,
  X,
} from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import { PageFrame, PageHeader, ErrorState, LoadingState } from '@/components/page-state'
import { StatusBadge } from '@/components/status-badge'
import { usePendingApprovals, useSession } from '@/features/sessions/use-session-data'
import { eventSummary, parsePayload, sessionIsActive, statusLabel, type SessionEvent } from '@/features/sessions/session-types'
import { useSessionEvents } from '@/features/sessions/use-session-events'
import {
  archiveSession,
  cancelRun,
  resolveApproval,
  startRun,
  type Approval,
  type Session,
} from '@/shared/api/client'
import { cn } from '@/lib/utils'
import { useMediaQuery } from '@/shared/ui/use-media-query'

type ReviewView = 'activity' | 'changes' | 'terminal' | 'review'

const EMPTY_APPROVALS: Approval[] = []

function eventIcon(type: string) {
  if (type.startsWith('todo_')) return <Check aria-hidden />
  if (type.includes('approval') || type.includes('policy')) return <ShieldCheck aria-hidden />
  if (type.includes('tool') || type.includes('command')) return <TerminalSquare aria-hidden />
  if (type.includes('change')) return <FileText aria-hidden />
  if (type.includes('compact')) return <Layers3 aria-hidden />
  if (type.includes('run_')) return <Play aria-hidden />
  return <Sparkles aria-hidden />
}

function formatEventTime(value: string): string {
  return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function EventTimelineItem({ event }: { event: SessionEvent }) {
  const payload = parsePayload(event.payload)
  const typeLabel = event.type.replace(/_/g, ' ')
  const detail = typeof payload.tool === 'string' ? payload.tool : typeof payload.path === 'string' ? payload.path : null
  return (
    <article className="timeline-event">
      <div className="timeline-event-icon">{eventIcon(event.type)}</div>
      <div className="timeline-event-body">
        <div className="timeline-event-meta"><strong>{typeLabel}</strong><time dateTime={event.occurredAtUtc}>{formatEventTime(event.occurredAtUtc)}</time><Badge variant="outline">#{event.sequence}</Badge></div>
        <p>{eventSummary(event)}</p>
        {detail ? <div className="event-detail-line"><Code2 aria-hidden />{detail}</div> : null}
      </div>
    </article>
  )
}

function ApprovalCard({
  approval,
  onResolve,
  disabled,
}: {
  approval: Approval
  onResolve: (decision: 'approve_once' | 'deny' | 'cancel_run') => void
  disabled: boolean
}) {
  return (
    <section className="approval-card" aria-label="Approval request">
      <div className="approval-top"><div className="approval-icon"><ShieldCheck aria-hidden /></div><div className="approval-heading"><span className="page-eyebrow">Permission request</span><h2>{approval.actionType.replace(/_/g, ' ')}</h2><p>{approval.summary}</p></div><Badge variant="destructive">Approval required</Badge></div>
      <div className="approval-details"><div><small>Affected files</small><strong>{approval.affectedPaths.length > 0 ? `${approval.affectedPaths.length} bounded path${approval.affectedPaths.length === 1 ? '' : 's'}` : 'No file paths disclosed'}</strong></div><div><small>Action ID</small><strong><code>{approval.id}</code></strong></div><div><small>Expires</small><strong>{new Date(approval.expiresAtUtc).toLocaleString()}</strong></div></div>
      {approval.affectedPaths.length > 0 ? <div className="approval-preview">{approval.affectedPaths.map((path) => <code key={path}>{path}</code>)}</div> : null}
      <div className="approval-actions"><Button variant="ghost" size="sm" disabled={disabled} onClick={() => onResolve('cancel_run')}>Cancel run</Button><Button variant="destructive" size="sm" disabled={disabled} onClick={() => onResolve('deny')}>Deny</Button><Button size="sm" disabled={disabled} onClick={() => onResolve('approve_once')}><Check data-icon="inline-start" />Approve once</Button></div>
    </section>
  )
}

function SessionComposer({
  session,
  instruction,
  setInstruction,
  onSubmit,
  submitting,
}: {
  session: Session
  instruction: string
  setInstruction: (value: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  submitting: boolean
}) {
  const canStart = !sessionIsActive(session) && !session.archived
  const submitLabel = session.status === 'draft' ? 'Start run' : 'Resume run'

  return (
    <form className="composer" onSubmit={onSubmit}>
      <label htmlFor="session-instruction" className="sr-only">Send a follow-up instruction</label>
      <textarea id="session-instruction" value={instruction} onChange={(event) => setInstruction(event.target.value)} placeholder="Ask SharpAgent to investigate, plan, or make a controlled change…" rows={3} disabled={!canStart || submitting} />
      <div className="composer-toolbar"><Button type="button" variant="ghost" size="icon-sm" aria-label="Attach context" disabled><Paperclip data-icon="inline-start" /></Button><Badge variant="outline">{session.mode === 'plan' ? 'Plan only' : 'Controlled execute'}</Badge><span className="composer-spacer" /><Button type="submit" disabled={!canStart || submitting}>{submitting ? 'Starting…' : submitLabel}<Send data-icon="inline-end" /></Button></div>
      <p className="composer-note">Writes and commands are shown for one-time approval. Credentials and raw environment values never enter the browser.</p>
    </form>
  )
}

function DetailsPanel({ session, events, onClose, showClose = true }: { session: Session; events: SessionEvent[]; onClose: () => void; showClose?: boolean }) {
  const todos = events.filter((event) => event.type === 'todo_created' || event.type === 'todo_updated')
  const latestRun = session.runs.at(-1)
  return (
    <aside className="session-details" aria-label="Session details">
      <div className="details-header"><div><p className="page-eyebrow">Session details</p><h2>Control plane</h2></div>{showClose ? <Button aria-label="Close details" variant="ghost" size="icon-sm" onClick={onClose}><X data-icon="inline-start" /></Button> : null}</div>
      <section className="details-section"><div className="details-section-heading"><h3>Plan</h3><StatusBadge status={session.status} /></div>{todos.length > 0 ? <div className="detail-todos">{todos.slice(-5).map((event) => <div key={`${event.sequence}-${event.type}`} className="detail-todo"><span aria-hidden className={cn('todo-dot', event.type === 'todo_updated' && 'done')} />{eventSummary(event)}</div>)}</div> : <p className="detail-muted">Plan events will appear here as the run progresses.</p>}</section>
      <section className="details-section"><div className="details-section-heading"><h3>Changes</h3><Button variant="ghost" size="xs" render={<Link to={`/sessions/${session.id}/changes`}>Review</Link>}>Review<ChevronRight data-icon="inline-end" /></Button></div><p className="detail-muted">Change evidence is server-owned and shown only after the run records it.</p></section>
      <section className="details-section"><div className="details-section-heading"><h3>Run usage</h3><Badge variant="outline">{latestRun ? statusLabel(latestRun.status) : 'Not started'}</Badge></div><dl className="detail-list"><div><dt>Runs</dt><dd>{session.runs.length}</dd></div><div><dt>Active run</dt><dd>{session.activeRunId ? 'Live' : 'None'}</dd></div><div><dt>Events received</dt><dd>{events.length}</dd></div><div><dt>Correlation</dt><dd className="detail-code" title={latestRun?.correlationId}>{latestRun?.correlationId ?? 'Request-scoped'}</dd></div><div><dt>Mode</dt><dd>{session.mode === 'plan' ? 'Plan only' : 'Controlled execute'}</dd></div></dl></section>
      <section className="details-section"><div className="details-section-heading"><h3>Safety</h3><ShieldCheck aria-hidden /></div><dl className="detail-list"><div><dt>Workspace</dt><dd>Server boundary</dd></div><div><dt>Execution</dt><dd>Isolated run scope</dd></div><div><dt>Network</dt><dd>Provider-only</dd></div><div><dt>Audit</dt><dd>Durable before live event</dd></div></dl></section>
    </aside>
  )
}

function ConfirmationOverlay({
  title,
  message,
  onCancel,
  onConfirm,
}: {
  title: string
  message: string
  onCancel: () => void
  onConfirm: () => void
}) {
  return <div className="confirm-overlay"><section className="confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-message"><div className="confirm-icon"><ShieldCheck aria-hidden /></div><h2 id="confirm-title">{title}</h2><p id="confirm-message">{message}</p><div className="form-actions-row"><Button variant="outline" onClick={onCancel}>Keep working</Button><Button variant="destructive" onClick={onConfirm}>Confirm</Button></div></section></div>
}

export function SessionWorkspacePage() {
  const { sessionId } = useParams()
  const navigate = useNavigate()
  const sessionQuery = useSession(sessionId)
  const approvalsQuery = usePendingApprovals(sessionId)
  const reloadSession = sessionQuery.reload
  const reloadApprovals = approvalsQuery.reload
  const [detailsOpen, setDetailsOpen] = useState(true)
  const [view, setView] = useState<ReviewView>('activity')
  const [instruction, setInstruction] = useState('')
  const [command, setCommand] = useState<'start' | 'cancel' | 'archive' | null>(null)
  const [commandError, setCommandError] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<'cancel' | 'archive' | null>(null)
  const isTablet = useMediaQuery('(max-width: 1020px)')
  const refreshProjection = useCallback(() => {
    reloadSession()
    reloadApprovals()
  }, [reloadApprovals, reloadSession])
  const eventStream = useSessionEvents(sessionId, refreshProjection)

  const session = sessionQuery.data
  const pendingApprovals = approvalsQuery.kind === 'ready' ? approvalsQuery.data : EMPTY_APPROVALS
  const approvalById = useMemo(() => new Map(pendingApprovals.map((approval) => [approval.id, approval])), [pendingApprovals])

  const runCommand = async (kind: 'start' | 'cancel' | 'archive') => {
    if (!sessionId || !session) return
    setCommand(kind)
    setCommandError(null)
    try {
      if (kind === 'start') {
        await startRun(sessionId, { instruction: instruction.trim() || null, resumeFromRunId: session.runs.at(-1)?.id ?? null })
        setInstruction('')
      } else if (kind === 'cancel') {
        await cancelRun(sessionId)
      } else {
        await archiveSession(sessionId)
        navigate('/sessions/archive')
        return
      }
      refreshProjection()
    } catch (cause: unknown) {
      setCommandError(cause instanceof Error ? cause.message : 'The command could not be completed.')
    } finally {
      setCommand(null)
      setConfirm(null)
    }
  }

  const handleComposerSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    void runCommand('start')
  }

  const handleApproval = async (approval: Approval, decision: 'approve_once' | 'deny' | 'cancel_run') => {
    setCommand('start')
    setCommandError(null)
    try {
      await resolveApproval(approval.id, { decision })
      refreshProjection()
    } catch (cause: unknown) {
      setCommandError(cause instanceof Error ? cause.message : 'The approval decision could not be recorded.')
    } finally {
      setCommand(null)
    }
  }

  if (sessionQuery.kind === 'loading' && !session) return <PageFrame><PageHeader eyebrow="Agent workspace" title="Session workspace" description="Loading the server-authoritative session projection and activity stream." /><LoadingState label="Loading session workspace…" /></PageFrame>
  if (sessionQuery.kind === 'error' || !session) return <PageFrame><PageHeader eyebrow="Agent workspace" title="Session workspace" description="The selected session could not be loaded." /><ErrorState message={sessionQuery.kind === 'error' ? sessionQuery.message : 'Session not found.'} onRetry={sessionQuery.reload} /></PageFrame>

  const timeline = eventStream.events
  const isActive = sessionIsActive(session)

  return (
    <div className="session-page">
      <header className="workspace-header">
        <div className="session-heading"><div className="breadcrumbs"><span>SharpAgent</span><span>/</span><span>{session.workspaceId}</span></div><h1>{session.task}</h1></div>
        <div className="workspace-actions"><Badge variant="outline"><Sparkles data-icon="inline-start" />{session.modelProfileId}</Badge><StatusBadge status={session.status} /><Button variant={view === 'activity' ? 'secondary' : 'ghost'} size="sm" onClick={() => setView('activity')}><Clock3 data-icon="inline-start" />Activity</Button><Button variant={view === 'review' ? 'secondary' : 'ghost'} size="sm" onClick={() => setView('review')}><ShieldCheck data-icon="inline-start" />Run controls</Button>{isTablet ? <Sheet open={detailsOpen} onOpenChange={setDetailsOpen}><SheetTrigger render={<Button variant={detailsOpen ? 'secondary' : 'ghost'} size="sm" />}><PanelIcon />Details</SheetTrigger><SheetContent side="right" className="session-details-sheet" closeLabel="Close details"><SheetTitle className="sr-only">Session details</SheetTitle><SheetDescription className="sr-only">Plan, changes, run usage, and safety details for this session.</SheetDescription><DetailsPanel session={session} events={timeline} showClose={false} onClose={() => setDetailsOpen(false)} /></SheetContent></Sheet> : <Button variant={detailsOpen ? 'secondary' : 'ghost'} size="sm" onClick={() => setDetailsOpen((open) => !open)}><PanelIcon />Details</Button>}</div>
      </header>
      {commandError ? <Alert variant="destructive" className="workspace-alert"><AlertTitle>Command needs attention</AlertTitle><AlertDescription>{commandError}</AlertDescription></Alert> : null}
      {eventStream.connection !== 'live' && isActive ? <div className="stream-banner" role="status"><RefreshCw aria-hidden />{eventStream.connection === 'error' ? eventStream.error ?? 'Activity stream interrupted; retrying.' : 'Reconnecting to the durable activity stream…'}</div> : null}
      {eventStream.hasGap ? <Alert className="workspace-alert"><RefreshCw /><AlertTitle>Activity replay requested a projection refresh</AlertTitle><AlertDescription>The server reported a sequence gap. The visible session state has been refreshed, and the stream will continue from the last verified event.</AlertDescription></Alert> : null}

      <div className={cn('conversation-layout', !detailsOpen && 'details-closed')}>
        <section className="conversation-panel">
          <div className="review-tabs" role="tablist" aria-label="Session review views"><button type="button" role="tab" aria-selected={view === 'activity'} className={view === 'activity' ? 'active' : ''} onClick={() => setView('activity')}>Activity</button><button type="button" role="tab" aria-selected={view === 'changes'} className={view === 'changes' ? 'active' : ''} onClick={() => setView('changes')}>Changes</button><button type="button" role="tab" aria-selected={view === 'terminal'} className={view === 'terminal' ? 'active' : ''} onClick={() => setView('terminal')}>Terminal</button><button type="button" role="tab" aria-selected={view === 'review'} className={view === 'review' ? 'active' : ''} onClick={() => setView('review')}>Final review</button></div>
          {view === 'activity' ? <>
            <div className="message-scroller" aria-live="polite">
              <div className="conversation-lane"><div className="timeline-marker">Today</div><article className="user-message"><div className="message-avatar">You</div><div><div className="message-meta"><strong>You</strong><time dateTime={session.createdAtUtc}>{formatEventTime(session.createdAtUtc)}</time></div><div className="user-bubble">{session.task}</div><div className="attachment-row"><Badge variant="outline"><FileText data-icon="inline-start" />Server-authoritative task</Badge></div></div></article>
                {timeline.length === 0 ? <div className="agent-intro"><div className="message-avatar agent"><Sparkles aria-hidden /></div><div><div className="message-meta"><strong>SharpAgent</strong><span>Waiting for run</span></div><p>Start the controlled run to receive safe status, plan, approval, tool, and review events here.</p></div></div> : null}
                {timeline.map((event) => {
                  const approvalId = parsePayload(event.payload).approvalId
                  const approval = typeof approvalId === 'string' ? approvalById.get(approvalId) : undefined
                  return <div key={`${event.sequence}-${event.eventId ?? event.type}`}>{<EventTimelineItem event={event} />}{approval ? <ApprovalCard approval={approval} disabled={command !== null} onResolve={(decision) => void handleApproval(approval, decision)} /> : null}</div>
                })}
              </div>
            </div>
            <SessionComposer session={session} instruction={instruction} setInstruction={setInstruction} onSubmit={handleComposerSubmit} submitting={command === 'start'} />
          </> : null}
          {view === 'changes' ? <div className="embedded-review-panel"><Code2 aria-hidden /><h2>Change review</h2><p>Open the focused changes route for file-level previews and validation evidence.</p><Button render={<Link to={`/sessions/${session.id}/changes`} />}>Open changes <ChevronRight data-icon="inline-end" /></Button></div> : null}
          {view === 'terminal' ? <div className="embedded-review-panel terminal-panel"><TerminalSquare aria-hidden /><h2>Bounded terminal evidence</h2><p>Only server-sanitized command output appears here. No general shell is exposed to the browser.</p><code>{timeline.filter((event) => event.type === 'tool_output').map(eventSummary).join('\n') || 'No terminal evidence recorded yet.'}</code></div> : null}
          {view === 'review' ? <div className="embedded-review-panel"><ShieldCheck aria-hidden /><h2>Run controls</h2><p>Every state-changing action is confirmed by the server and retained in the audit history.</p><div className="control-card-list">{isActive ? <Button variant="destructive" onClick={() => setConfirm('cancel')} disabled={command !== null}>Cancel active run</Button> : <Button onClick={() => void runCommand('start')} disabled={command !== null || session.archived}><Play data-icon="inline-start" />{session.status === 'draft' ? 'Start run' : 'Resume run'}</Button>}{!isActive && !session.archived ? <Button variant="outline" onClick={() => setConfirm('archive')} disabled={command !== null}>Archive session</Button> : null}</div></div> : null}
        </section>
        {!isTablet && detailsOpen ? <DetailsPanel session={session} events={timeline} onClose={() => setDetailsOpen(false)} /> : null}
      </div>
      {confirm === 'cancel' ? <ConfirmationOverlay title="Cancel this run?" message="The server will request a cooperative stop at the next safe checkpoint and preserve the audit history." onCancel={() => setConfirm(null)} onConfirm={() => void runCommand('cancel')} /> : null}
      {confirm === 'archive' ? <ConfirmationOverlay title="Archive this session?" message="Archiving hides the inactive session from the active list. Audit history, changes, and results remain durable." onCancel={() => setConfirm(null)} onConfirm={() => void runCommand('archive')} /> : null}
    </div>
  )
}

function PanelIcon() {
  return <FolderSearch data-icon="inline-start" />
}
