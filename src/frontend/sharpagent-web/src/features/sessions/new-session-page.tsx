import { useMemo, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { ArrowLeft, CircleHelp, Play, ShieldCheck } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { PageFrame, PageHeader, ErrorState, LoadingState } from '@/components/page-state'
import { useCatalog } from '@/features/catalog/use-catalog'
import { createSession, type SessionMode } from '@/shared/api/client'

const EMPTY_WORKSPACES = [] as const
const EMPTY_PROFILES = [] as const
const EMPTY_POLICIES = [] as const

export function NewSessionPage() {
  const navigate = useNavigate()
  const catalog = useCatalog()
  const [workspaceId, setWorkspaceId] = useState('')
  const [modelProfileId, setModelProfileId] = useState('')
  const [policyProfileId, setPolicyProfileId] = useState('')
  const [mode, setMode] = useState<SessionMode>('plan')
  const [task, setTask] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

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
  const selectedProfileEligible = effectiveModelProfileId.length > 0

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!effectiveWorkspaceId || !task.trim() || !effectiveModelProfileId || !effectivePolicyProfileId || !selectedProfileEligible) {
      setError('Choose a valid workspace, eligible model profile, policy, and task before creating the session.')
      return
    }

    setSubmitting(true)
    setError(null)
    try {
      const session = await createSession({
        workspaceId: effectiveWorkspaceId,
        task: task.trim(),
        mode,
        modelProfileId: effectiveModelProfileId,
        policyProfileId: effectivePolicyProfileId,
      })
      navigate(`/sessions/${session.id}`)
    } catch (cause: unknown) {
      setError(cause instanceof Error ? cause.message : 'The session could not be created.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <PageFrame className="form-page">
      <PageHeader
        eyebrow="Agent workspace"
        title="New session"
        description="Start with a provider-neutral profile and a policy boundary. The server decides execution eligibility."
        actions={<Button variant="ghost" render={<Link to="/" />}><ArrowLeft data-icon="inline-start" />Back to dashboard</Button>}
      />

      {catalog.kind === 'loading' ? <LoadingState label="Loading validated workspaces and profiles…" /> : null}
      {catalog.kind === 'error' ? <ErrorState message={catalog.message} onRetry={catalog.reload} /> : null}
      {error ? <Alert variant="destructive"><AlertTitle>Session not created</AlertTitle><AlertDescription>{error}</AlertDescription></Alert> : null}
      {catalog.kind === 'ready' && (workspaces.length === 0 || profiles.length === 0 || policies.length === 0) ? (
        <Alert>
          <AlertTitle>Complete local setup</AlertTitle>
          <AlertDescription>
            {workspaces.length === 0 ? 'Register a trusted workspace before creating a session. ' : null}
            {profiles.length === 0 ? 'A validated model profile is required. ' : null}
            {policies.length === 0 ? 'A policy profile is required.' : null}
            <div className="form-actions-row">
              {workspaces.length === 0 ? <Button variant="outline" render={<Link to="/settings/workspaces" />}>Register workspace</Button> : null}
              {profiles.length === 0 ? <Button variant="outline" render={<Link to="/settings/models" />}>Review model profiles</Button> : null}
              {policies.length === 0 ? <Button variant="outline" render={<Link to="/settings/policy" />}>Review policy</Button> : null}
            </div>
          </AlertDescription>
        </Alert>
      ) : null}

      {catalog.kind === 'ready' ? <form className="session-form" onSubmit={handleSubmit}>
        <Card>
          <CardHeader><CardTitle>Task setup</CardTitle><CardDescription>Use the same safe selectors the API contract exposes. Credentials never enter the browser.</CardDescription></CardHeader>
          <CardContent className="form-stack">
            <label className="form-field"><span>Task</span><textarea aria-label="Task" value={task} onChange={(event) => setTask(event.target.value)} rows={5} placeholder="Describe what SharpAgent should investigate, plan, or repair…" required /></label>
            <div className="form-grid">
              <label className="form-field"><span>Workspace</span><select aria-label="Workspace" value={effectiveWorkspaceId} onChange={(event) => setWorkspaceId(event.target.value)} required><option value="" disabled>Select a registered workspace</option>{workspaces.map((workspace) => <option key={workspace.id} value={workspace.id}>{workspace.name} · {workspace.status}</option>)}</select></label>
              <label className="form-field"><span>Policy and limits</span><select aria-label="Policy and limits" value={effectivePolicyProfileId} onChange={(event) => setPolicyProfileId(event.target.value)} required><option value="" disabled>Select a policy profile</option>{policies.map((policy) => <option key={policy.id} value={policy.id}>{policy.name} · {policy.maxToolCalls} tool calls</option>)}</select></label>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>Run mode</CardTitle><CardDescription>Execute mode can propose writes and commands, but every sensitive action stays policy- and approval-gated.</CardDescription></CardHeader>
          <CardContent className="form-stack">
            <div className="mode-choice-group" role="radiogroup" aria-label="Run mode">
              <button type="button" role="radio" aria-checked={mode === 'plan'} className={mode === 'plan' ? 'mode-choice active' : 'mode-choice'} onClick={() => setMode('plan')}><CircleHelp /><span><strong>Plan only</strong><small>Read and search without side effects.</small></span></button>
              <button type="button" role="radio" aria-checked={mode === 'execute'} className={mode === 'execute' ? 'mode-choice active' : 'mode-choice'} onClick={() => setMode('execute')}><ShieldCheck /><span><strong>Controlled execute</strong><small>Writes and commands require policy decisions and one-time approval.</small></span></button>
            </div>
            <label className="form-field"><span>Model profile</span><select aria-label="Model profile" value={effectiveModelProfileId} onChange={(event) => setModelProfileId(event.target.value)} required><option value="" disabled>Select an eligible profile</option>{profiles.map((profile) => <option key={profile.id} value={profile.id} disabled={!profile.enabled || !eligibleProfiles.some((candidate) => candidate.id === profile.id)}>{profile.displayName} · {profile.validationStatus}{profile.eligibleForExecute ? '' : ' · plan only'}</option>)}</select></label>
          </CardContent>
        </Card>

        <div className="form-actions-row"><Button type="button" variant="outline" render={<Link to="/" />}>Cancel</Button><Button type="submit" disabled={submitting || !selectedProfileEligible || !task.trim()}>{submitting ? 'Creating…' : 'Create session'}<Play data-icon="inline-end" /></Button></div>
      </form> : null}
    </PageFrame>
  )
}
