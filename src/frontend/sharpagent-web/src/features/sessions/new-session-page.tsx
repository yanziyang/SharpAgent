import { useMemo, useState, type FormEvent } from 'react'
import { ArrowLeft } from 'lucide-react'
import { Link, useNavigate } from 'react-router'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { MessageScroller } from '@/components/ui/message-scroller'
import { ChatComposer } from '@/features/sessions/chat-composer'
import { useCatalog } from '@/features/catalog/use-catalog'
import { createSession, startRun, type SessionMode } from '@/shared/api/client'

const EMPTY_WORKSPACES = [] as const
const EMPTY_PROFILES = [] as const
const EMPTY_POLICIES = [] as const

export function NewSessionPage() {
  const navigate = useNavigate()
  const catalog = useCatalog()
  const [message, setMessage] = useState('')
  const [mode, setMode] = useState<SessionMode>('plan')
  const [workspaceId, setWorkspaceId] = useState('')
  const [modelProfileId, setModelProfileId] = useState('')
  const [policyProfileId, setPolicyProfileId] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [createdSessionId, setCreatedSessionId] = useState<string | null>(null)

  const workspaces = catalog.kind === 'ready' ? catalog.data.workspaces : EMPTY_WORKSPACES
  const profiles = catalog.kind === 'ready' ? catalog.data.modelProfiles : EMPTY_PROFILES
  const policies = catalog.kind === 'ready' ? catalog.data.policyProfiles : EMPTY_POLICIES
  const eligibleProfiles = useMemo(
    () => profiles.filter((profile) => profile.enabled && (mode === 'plan' ? profile.eligibleForPlan : profile.eligibleForExecute)),
    [mode, profiles],
  )
  const effectiveWorkspaceId = workspaceId || workspaces[0]?.id || ''
  const effectivePolicyProfileId = policyProfileId || policies[0]?.id || ''
  const effectiveModelProfileId = modelProfileId && eligibleProfiles.some((profile) => profile.id === modelProfileId)
    ? modelProfileId
    : eligibleProfiles[0]?.id || ''
  const readyToStart = catalog.kind === 'ready'
    && Boolean(effectiveWorkspaceId && effectivePolicyProfileId && effectiveModelProfileId && message.trim())

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const task = message.trim()
    if (!task) {
      setError('Write a message before starting the conversation.')
      return
    }

    if (!readyToStart) {
      setError('Session controls are not ready. Confirm a workspace, model, and security policy before sending.')
      return
    }

    setSubmitting(true)
    setError(null)
    setCreatedSessionId(null)
    try {
      const session = await createSession({
        workspaceId: effectiveWorkspaceId,
        task,
        mode,
        modelProfileId: effectiveModelProfileId,
        policyProfileId: effectivePolicyProfileId,
      })
      setCreatedSessionId(session.id)
      await startRun(session.id, { instruction: null, resumeFromRunId: null })
      navigate(`/sessions/${session.id}`, { replace: true })
    } catch (cause: unknown) {
      setError(cause instanceof Error ? cause.message : 'The conversation could not be started.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="chat-page new-session-chat-page">
      <header className="chat-header">
        <Button variant="ghost" size="sm" render={<Link to="/" />}><ArrowLeft data-icon="inline-start" />Back</Button>
        <div className="chat-header-copy"><p className="page-eyebrow">SharpAgent chat</p><h1>Conversation</h1></div>
        <div className="chat-header-actions"><Badge variant="secondary">New session</Badge></div>
      </header>

      {error ? <Alert variant="destructive" className="chat-alert"><AlertTitle>Chat needs attention</AlertTitle><AlertDescription>{error}{createdSessionId ? <Button variant="outline" size="sm" render={<Link to={`/sessions/${createdSessionId}`} />}>Open conversation</Button> : null}</AlertDescription></Alert> : null}
      {catalog.kind === 'error' ? <Alert variant="destructive" className="chat-alert"><AlertTitle>Unable to load session controls</AlertTitle><AlertDescription>{catalog.message}<Button variant="outline" size="sm" onClick={catalog.reload}>Retry</Button></AlertDescription></Alert> : null}

      <main className="chat-main">
        <MessageScroller aria-label="Conversation messages">
          <div className="chat-lane">
            <div className="chat-marker">Conversation</div>
          </div>
        </MessageScroller>

        <ChatComposer
          value={message}
          onChange={setMessage}
          onSubmit={handleSubmit}
          ariaLabel="Message"
          placeholder="Ask anything, / for commands, @ for context…"
          mode={mode}
          onModeChange={setMode}
          modelProfileId={effectiveModelProfileId}
          modelProfiles={eligibleProfiles}
          onModelChange={setModelProfileId}
          workspaceId={effectiveWorkspaceId}
          workspaces={workspaces}
          onWorkspaceChange={setWorkspaceId}
          policyProfileId={effectivePolicyProfileId}
          policyProfiles={policies}
          onPolicyChange={setPolicyProfileId}
          submitting={submitting}
          disabled={catalog.kind !== 'ready'}
          canSubmit={readyToStart}
          submitLabel="Send"
        />
      </main>
    </div>
  )
}
