import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ArrowLeft, Check, ShieldCheck, Sparkles } from 'lucide-react'
import { Link, useNavigate, useParams } from 'react-router'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Bubble, Message, MessageScroller } from '@/components/ui/message-scroller'
import { ErrorState, LoadingState, PageFrame, PageHeader } from '@/components/page-state'
import { usePendingApprovals, useSession } from '@/features/sessions/use-session-data'
import { ChatComposer } from '@/features/sessions/chat-composer'
import { useCatalog } from '@/features/catalog/use-catalog'
import { parsePayload, sessionIsActive, type SessionEvent } from '@/features/sessions/session-types'
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

type ChatMessage = {
  id: string
  role: 'user' | 'assistant'
  text: string
  runId?: string | null
}

type Command = 'start' | 'cancel' | 'archive' | 'approval' | null

const EMPTY_APPROVALS: Approval[] = []
const PROJECTION_REFRESH_EVENTS = new Set(['run_completed', 'run_failed', 'run_cancelled', 'status'])

function payloadText(event: SessionEvent, key: string, preserveWhitespace = false): string | null {
  const value = parsePayload(event.payload)[key]
  return typeof value === 'string' && value.trim().length > 0 ? preserveWhitespace ? value : value.trim() : null
}

function removePendingInstruction(pending: string[], instruction: string): string[] {
  const index = pending.indexOf(instruction)
  if (index < 0) {
    return pending
  }

  return [...pending.slice(0, index), ...pending.slice(index + 1)]
}

function chatMessagesFromEvents(session: Session, events: SessionEvent[], pendingInstructions: string[] = []): ChatMessage[] {
  const messages: ChatMessage[] = [{ id: `task-${session.id}`, role: 'user', text: session.task }]
  let pending = [...pendingInstructions]
  const assistantRuns = new Set<string>()
  const assistantMessageIndexes = new Map<string, number>()

  for (const event of events) {
    if (event.type === 'run_started') {
      const instruction = payloadText(event, 'instruction')
      if (instruction) {
        messages.push({ id: `user-${event.sequence}`, role: 'user', text: instruction })
        pending = removePendingInstruction(pending, instruction)
      }
      continue
    }

    if (event.type === 'assistant_summary') {
      const summary = payloadText(event, 'summary', true)
      if (summary) {
        if (event.runId) {
          assistantRuns.add(event.runId)
        }
        const runKey = event.runId ?? 'unscoped'
        const existingIndex = assistantMessageIndexes.get(runKey)
        const existing = existingIndex === undefined ? undefined : messages[existingIndex]
        if (existingIndex !== undefined && existing) {
          messages[existingIndex] = { ...existing, text: `${existing.text}${summary}` }
        } else {
          assistantMessageIndexes.set(runKey, messages.length)
          messages.push({ id: `assistant-${event.runId ?? event.sequence}`, role: 'assistant', text: summary, runId: event.runId })
        }
      }
      continue
    }

    if (event.type === 'run_completed' && event.runId && !assistantRuns.has(event.runId)) {
      const summary = payloadText(event, 'summary', true)
      if (summary) {
        messages.push({ id: `assistant-${event.sequence}`, role: 'assistant', text: summary, runId: event.runId })
      }
      continue
    }

    if (event.type === 'run_failed') {
      const reason = payloadText(event, 'reason')
      if (reason) {
        messages.push({ id: `assistant-${event.sequence}`, role: 'assistant', text: reason, runId: event.runId })
      }
    }
  }

  for (const instruction of pending) {
    messages.push({ id: `pending-${instruction}`, role: 'user', text: instruction })
  }

  return messages
}

function ChatMessageRow({ message, streaming }: { message: ChatMessage; streaming: boolean }) {
  const isUser = message.role === 'user'
  return (
    <Message role={message.role}>
      <div className={cn('chat-avatar', isUser ? 'chat-avatar-user' : 'chat-avatar-agent')}>
        {isUser ? 'You' : <Sparkles aria-hidden />}
      </div>
      <div className="chat-message-content">
        <div className="chat-message-meta"><strong>{isUser ? 'You' : 'SharpAgent'}</strong>{streaming ? <Badge variant="secondary">Streaming</Badge> : null}</div>
        <Bubble>{message.text}{streaming ? <span className="stream-cursor" aria-label="Response is streaming">▍</span> : null}</Bubble>
      </div>
    </Message>
  )
}

function ApprovalPrompt({
  approval,
  disabled,
  onResolve,
}: {
  approval: Approval
  disabled: boolean
  onResolve: (decision: 'approve_once' | 'deny' | 'cancel_run') => void
}) {
  return (
    <section className="chat-approval" aria-label="Approval required">
      <div className="chat-approval-heading"><ShieldCheck aria-hidden /><div><Badge variant="destructive">Approval required</Badge><p>{approval.summary}</p></div></div>
      {approval.affectedPaths.length > 0 ? <div className="chat-approval-paths">{approval.affectedPaths.map((path) => <code key={path}>{path}</code>)}</div> : null}
      <div className="chat-approval-actions"><Button variant="ghost" size="sm" disabled={disabled} onClick={() => onResolve('cancel_run')}>Stop</Button><Button variant="destructive" size="sm" disabled={disabled} onClick={() => onResolve('deny')}>Deny</Button><Button size="sm" disabled={disabled} onClick={() => onResolve('approve_once')}><Check data-icon="inline-start" />Approve once</Button></div>
    </section>
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
  return <div className="confirm-overlay"><section className="confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-message"><div className="confirm-icon"><ShieldCheck aria-hidden /></div><h2 id="confirm-title">{title}</h2><p id="confirm-message">{message}</p><div className="form-actions-row"><Button variant="outline" onClick={onCancel}>Keep chatting</Button><Button variant="destructive" onClick={onConfirm}>Confirm</Button></div></section></div>
}

export function SessionWorkspacePage() {
  const { sessionId } = useParams()
  const navigate = useNavigate()
  const sessionQuery = useSession(sessionId)
  const approvalsQuery = usePendingApprovals(sessionId)
  const catalog = useCatalog()
  const reloadSession = sessionQuery.reload
  const reloadApprovals = approvalsQuery.reload
  const [instruction, setInstruction] = useState('')
  const [pendingInstructions, setPendingInstructions] = useState<string[]>([])
  const [command, setCommand] = useState<Command>(null)
  const [commandError, setCommandError] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<'cancel' | 'archive' | null>(null)
  const refreshedSequence = useRef(0)
  const refreshProjection = useCallback(() => {
    reloadSession()
    reloadApprovals()
  }, [reloadApprovals, reloadSession])
  const eventStream = useSessionEvents(sessionId, refreshProjection)

  const session = sessionQuery.data
  const pendingApprovals = approvalsQuery.kind === 'ready' ? approvalsQuery.data : EMPTY_APPROVALS
  const isActive = session ? sessionIsActive(session) : false
  const messages = useMemo(
    () => session ? chatMessagesFromEvents(session, eventStream.events, pendingInstructions) : [],
    [eventStream.events, pendingInstructions, session],
  )
  const hasAssistantResponse = messages.some((message) => message.role === 'assistant')

  useEffect(() => {
    const projectionEvent = [...eventStream.events].reverse().find((event) => PROJECTION_REFRESH_EVENTS.has(event.type))
    if (projectionEvent && projectionEvent.sequence > refreshedSequence.current) {
      refreshedSequence.current = projectionEvent.sequence
      refreshProjection()
    }
  }, [eventStream.events, refreshProjection])

  const runCommand = async (kind: Exclude<Command, null>, value = '') => {
    if (!sessionId || !session) return
    setCommand(kind)
    setCommandError(null)
    let pendingValue = ''
    try {
      if (kind === 'start') {
        pendingValue = value.trim()
        if (pendingValue) {
          setPendingInstructions((current) => [...current, pendingValue])
        }
        await startRun(sessionId, { instruction: pendingValue || null, resumeFromRunId: session.runs.at(-1)?.id ?? null })
        setInstruction('')
      } else if (kind === 'cancel') {
        await cancelRun(sessionId)
      } else if (kind === 'archive') {
        await archiveSession(sessionId)
        navigate('/sessions/archive')
        return
      }
      refreshProjection()
    } catch (cause: unknown) {
      if (pendingValue) {
        setPendingInstructions((current) => removePendingInstruction(current, pendingValue))
      }
      setCommandError(cause instanceof Error ? cause.message : 'The chat command could not be completed.')
    } finally {
      setCommand(null)
      setConfirm(null)
    }
  }

  const handleComposerSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    void runCommand('start', instruction)
  }

  const handleApproval = async (approval: Approval, decision: 'approve_once' | 'deny' | 'cancel_run') => {
    setCommand('approval')
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

  if (sessionQuery.kind === 'loading' && !session) return <PageFrame><PageHeader eyebrow="SharpAgent chat" title="Chat" description="Loading the conversation." /><LoadingState label="Loading chat…" /></PageFrame>
  if (sessionQuery.kind === 'error' || !session) return <PageFrame><PageHeader eyebrow="SharpAgent chat" title="Chat" description="The selected conversation could not be loaded." /><ErrorState message={sessionQuery.kind === 'error' ? sessionQuery.message : 'Session not found.'} onRetry={sessionQuery.reload} /></PageFrame>

  const lastMessage = messages.at(-1)
  const modelProfile = catalog.kind === 'ready'
    ? catalog.data.modelProfiles.find((profile) => profile.id === session.modelProfileId)
    : undefined
  const workspace = catalog.kind === 'ready'
    ? catalog.data.workspaces.find((candidate) => candidate.id === session.workspaceId)
    : undefined
  const policy = catalog.kind === 'ready'
    ? catalog.data.policyProfiles.find((candidate) => candidate.id === session.policyProfileId)
    : undefined

  return (
    <div className="chat-page">
      <header className="chat-header">
        <Button variant="ghost" size="sm" render={<Link to="/" />}><ArrowLeft data-icon="inline-start" />Back</Button>
        <div className="chat-header-copy"><p className="page-eyebrow">SharpAgent chat</p><h1>Conversation</h1></div>
        <div className="chat-header-actions">
          {isActive ? <Badge variant="secondary">Responding</Badge> : null}
          {!isActive && !session.archived ? <Button variant="ghost" size="sm" onClick={() => setConfirm('archive')}>Archive</Button> : null}
        </div>
      </header>
      {commandError ? <Alert variant="destructive" className="chat-alert"><AlertTitle>Chat needs attention</AlertTitle><AlertDescription>{commandError}</AlertDescription></Alert> : null}
      {eventStream.connection !== 'live' && isActive ? <div className="chat-stream-status" role="status">{eventStream.connection === 'error' ? eventStream.error ?? 'The response stream was interrupted; retrying.' : 'Connecting to the response stream…'}</div> : null}

      <main className="chat-main">
        <MessageScroller aria-label="Conversation messages">
          <div className="chat-lane">
            <div className="chat-marker">Conversation</div>
            {messages.map((message) => <ChatMessageRow key={message.id} message={message} streaming={isActive && message.role === 'assistant' && message.id === lastMessage?.id} />)}
            {isActive && (!hasAssistantResponse || lastMessage?.role === 'user') ? <Message role="assistant"><div className="chat-avatar chat-avatar-agent"><Sparkles aria-hidden /></div><div className="chat-message-content"><div className="chat-message-meta"><strong>SharpAgent</strong><Badge variant="secondary">Streaming</Badge></div><Bubble><span className="thinking-dots" aria-label="SharpAgent is responding">SharpAgent is responding…</span></Bubble></div></Message> : null}
            {pendingApprovals.map((approval) => <ApprovalPrompt key={approval.id} approval={approval} disabled={command !== null} onResolve={(decision) => void handleApproval(approval, decision)} />)}
          </div>
        </MessageScroller>
        <ChatComposer
          value={instruction}
          onChange={setInstruction}
          onSubmit={handleComposerSubmit}
          ariaLabel="Send a follow-up message"
          placeholder={session.status === 'draft' ? 'Ask anything, / for commands, @ for context…' : 'Ask a follow-up question…'}
          mode={session.mode}
          modelLabel={modelProfile?.displayName ?? session.modelProfileId}
          workspaceLabel={workspace?.name ?? session.workspaceId}
          policyLabel={policy?.name ?? session.policyProfileId}
          submitting={command !== null}
          active={isActive}
          onCancel={() => setConfirm('cancel')}
          canSubmit={!sessionIsActive(session) && !session.archived && (session.status === 'draft' || instruction.trim().length > 0)}
          archived={session.archived}
        />
      </main>

      {confirm === 'cancel' ? <ConfirmationOverlay title="Stop this response?" message="SharpAgent will request a cooperative stop and keep the conversation history." onCancel={() => setConfirm(null)} onConfirm={() => void runCommand('cancel')} /> : null}
      {confirm === 'archive' ? <ConfirmationOverlay title="Archive this conversation?" message="The conversation will leave the active list, while its audit history remains durable." onCancel={() => setConfirm(null)} onConfirm={() => void runCommand('archive')} /> : null}
    </div>
  )
}
