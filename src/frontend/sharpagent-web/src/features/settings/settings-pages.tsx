import { useState, type FormEvent } from 'react'
import { Check, Palette, Plus } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { PageFrame, PageHeader, ErrorState, LoadingState } from '@/components/page-state'
import { HealthStatusCard } from '@/features/health/health-status-card'
import { useHealth } from '@/features/health/use-health'
import { useCatalog } from '@/features/catalog/use-catalog'
import { apiCommand, type ModelProfile, type PolicyProfile, type Workspace } from '@/shared/api/client'
import type { ResourceState } from '@/shared/api/use-resource'
import { useTheme } from '@/shared/theme/use-theme'
import { THEME_LABELS, THEMES } from '@/shared/theme/themes'

export function WorkspacesSettingsPage() {
  const catalog = useCatalog()
  const [name, setName] = useState('')
  const [rootPath, setRootPath] = useState('')
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const register = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setSaving(true)
    setMessage(null)
    try {
      await apiCommand('/api/workspaces', 'POST', { name: name.trim(), rootPath: rootPath.trim() }, 'register-workspace')
      setName('')
      setRootPath('')
      setMessage('Workspace registration submitted. Refreshing the server catalog.')
      catalog.reload()
    } catch (cause: unknown) {
      setMessage(cause instanceof Error ? cause.message : 'The workspace could not be registered.')
    } finally {
      setSaving(false)
    }
  }

  return <PageFrame><PageHeader eyebrow="Administration" title="Workspace settings" description="Register trusted local roots. The server validates and canonicalizes paths before they can be used." />
    {message ? <Alert><AlertTitle>Workspace update</AlertTitle><AlertDescription>{message}</AlertDescription></Alert> : null}
    <Card><CardHeader><CardTitle>Register workspace</CardTitle><CardDescription>Only a display name and local path are sent; no provider credentials or unrestricted environment values are exposed.</CardDescription></CardHeader><CardContent><form className="form-grid" onSubmit={register}><label className="form-field"><span>Name</span><input aria-label="Workspace name" value={name} onChange={(event) => setName(event.target.value)} required placeholder="SharpAgent / storefront" /></label><label className="form-field"><span>Root path</span><input aria-label="Workspace root path" value={rootPath} onChange={(event) => setRootPath(event.target.value)} required placeholder="C:\\work\\storefront" /></label><Button type="submit" disabled={saving || !name.trim() || !rootPath.trim()}><Plus data-icon="inline-start" />{saving ? 'Registering…' : 'Register workspace'}</Button></form></CardContent></Card>
    <SettingsCatalogState state={selectCatalog<Workspace>(catalog, 'workspaces')} title="Registered workspaces" empty="No workspaces are registered yet." render={(item) => <div className="settings-row" key={item.id}><div><strong>{item.name}</strong><p>{item.rootPath}</p></div><Badge variant={item.status === 'validated' ? 'secondary' : 'outline'}>{item.status}</Badge></div>} />
  </PageFrame>
}

export function ModelsSettingsPage() {
  const catalog = useCatalog()
  return <PageFrame><PageHeader eyebrow="Administration" title="Model profiles" description="Provider-neutral profiles are selectable only when the server records compatible validated capabilities." /><SettingsCatalogState state={selectCatalog<ModelProfile>(catalog, 'modelProfiles')} title="Available profiles" empty="No model profiles are configured." render={(item) => <div className="settings-row" key={item.id}><div><strong>{item.displayName}</strong><p>{item.provider} · {item.validationStatus}</p></div><div className="settings-badges"><Badge variant={item.eligibleForPlan ? 'secondary' : 'outline'}>{item.eligibleForPlan ? 'Plan' : 'Plan unavailable'}</Badge><Badge variant={item.eligibleForExecute ? 'secondary' : 'outline'}>{item.eligibleForExecute ? 'Execute' : 'Plan only'}</Badge></div></div>} /></PageFrame>
}

export function PolicySettingsPage() {
  const catalog = useCatalog()
  return <PageFrame><PageHeader eyebrow="Administration" title="Policy and limits" description="Run limits and approval expiry are displayed from the server policy registry." /><SettingsCatalogState state={selectCatalog<PolicyProfile>(catalog, 'policyProfiles')} title="Policy profiles" empty="No policy profiles are configured." render={(item) => <div className="settings-row" key={item.id}><div><strong>{item.name}</strong><p>{item.maxRunDurationMinutes} minute duration · {item.approvalExpiryMinutes} minute approval expiry</p></div><div className="settings-badges"><Badge variant="outline">{item.maxToolCalls} tools</Badge><Badge variant="outline">${item.maxEstimatedCostUsd.toFixed(2)}</Badge></div></div>} /></PageFrame>
}

export function RuntimeSettingsPage() {
  const state = useHealth()
  return <PageFrame><PageHeader eyebrow="Administration" title="Runtime health" description="Read-only readiness information for the local service. Details remain bounded and sanitized." />{state.kind === 'loading' ? <LoadingState label="Checking runtime health…" /> : null}{state.kind === 'error' ? <ErrorState message={state.message} /> : null}{state.kind === 'ok' ? <HealthStatusCard snapshot={state.snapshot} /> : null}</PageFrame>
}

export function AppearanceSettingsPage() {
  const { theme, setTheme } = useTheme()
  return <PageFrame><PageHeader eyebrow="Preferences" title="Appearance" description="Theme and layout preferences stay local to this browser. Server state and event history are never stored here." /><Card><CardHeader><CardTitle>Theme</CardTitle><CardDescription>Choose one of the four approved semantic palettes.</CardDescription></CardHeader><CardContent><div className="theme-grid">{THEMES.map((id) => <button key={id} type="button" className={theme === id ? 'theme-card active' : 'theme-card'} aria-pressed={theme === id} onClick={() => setTheme(id)}><span className={`theme-preview theme-preview-${id}`}><span /><span /><span /></span><span className="theme-card-copy"><strong>{THEME_LABELS[id]}</strong>{theme === id ? <Check aria-hidden /> : null}</span></button>)}</div></CardContent></Card><Alert><Palette /><AlertTitle>Local-only preference</AlertTitle><AlertDescription>Changing theme does not change server sessions, approvals, provider configuration, or audit history.</AlertDescription></Alert></PageFrame>
}

type CatalogKey = 'workspaces' | 'modelProfiles' | 'policyProfiles'
type CatalogState = ReturnType<typeof useCatalog>

function selectCatalog<T extends { id: string }>(state: CatalogState, select: CatalogKey): ResourceState<T[]> & { reload: () => void } {
  if (state.kind === 'loading') return { kind: 'loading', data: null, reload: state.reload }
  if (state.kind === 'error') return { kind: 'error', data: null, message: state.message, reload: state.reload }
  return { kind: 'ready', data: state.data[select] as unknown as T[], reload: state.reload }
}

function SettingsCatalogState<T extends { id: string }>({
  state,
  title,
  empty,
  render,
}: {
  state: ResourceState<T[]> & { reload: () => void }
  title: string
  empty: string
  render: (item: T) => React.ReactNode
}) {
  if (state.kind === 'loading') return <LoadingState label={`Loading ${title.toLowerCase()}…`} />
  if (state.kind === 'error') return <ErrorState message={state.message} onRetry={state.reload} />
  const items = state.data
  return <section className="settings-list" aria-labelledby={`${title}-heading`}><div className="section-heading-row"><div><p className="page-eyebrow">Server registry</p><h2 id={`${title}-heading`}>{title}</h2></div><Badge variant="outline">{items.length} configured</Badge></div>{items.length === 0 ? <Card><CardContent className="detail-muted">{empty}</CardContent></Card> : <Card><CardContent className="settings-row-list">{items.map(render)}</CardContent></Card>}</section>
}
