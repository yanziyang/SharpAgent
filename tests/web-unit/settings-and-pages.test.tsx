import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { StatisticsPage } from '@/features/dashboard/statistics-page'
import { ArchivePage } from '@/features/sessions/archive-page'
import { ChangesPage } from '@/features/sessions/changes-page'
import { NewSessionPage } from '@/features/sessions/new-session-page'
import {
  AppearanceSettingsPage,
  ModelsSettingsPage,
  PolicySettingsPage,
  RuntimeSettingsPage,
  WorkspacesSettingsPage,
} from '@/features/settings/settings-pages'
import { ThemeProvider } from '@/shared/theme/theme-provider'

const session = {
  id: 'ses_1',
  task: 'Inspect the parser',
  mode: 'plan',
  status: 'completed',
  workspaceId: 'ws_1',
  modelProfileId: 'model_1',
  activeRunId: null,
  archived: false,
  createdAtUtc: '2026-08-25T09:00:00Z',
  updatedAtUtc: '2026-08-25T09:10:00Z',
}

const executeSession = { ...session, id: 'ses_execute', task: 'Execute the bounded task', mode: 'execute' }

const archivedSession = { ...session, id: 'ses_archived', task: 'Review archived task', archived: true, status: 'cancelled' }

const workspace = {
  id: 'ws_1',
  name: 'SharpAgent / storefront',
  rootPath: 'C:\\work\\storefront',
  status: 'validated',
  validationMessage: null,
  createdAtUtc: '2026-08-25T09:00:00Z',
  updatedAtUtc: '2026-08-25T09:00:00Z',
}

const modelProfiles = [
  {
    id: 'model_1', provider: 'DeepSeek', displayName: 'DeepSeek Coder', enabled: true,
    validationStatus: 'validated', streaming: true, toolCalling: true, contextWindowTokens: 64000,
    estimatedUsdPerMillionInputTokens: 0.14, estimatedUsdPerMillionOutputTokens: 0.28,
    eligibleForPlan: true, eligibleForExecute: true,
  },
  {
    id: 'model_plan', provider: 'OpenRouter', displayName: 'OpenRouter plan-only', enabled: true,
    validationStatus: 'validated', streaming: true, toolCalling: false, contextWindowTokens: 32000,
    estimatedUsdPerMillionInputTokens: null, estimatedUsdPerMillionOutputTokens: null,
    eligibleForPlan: true, eligibleForExecute: false,
  },
]

const policyProfiles = [{
  id: 'policy_1', name: 'Balanced local policy', maxRunDurationMinutes: 30,
  maxToolCalls: 40, maxEstimatedCostUsd: 2.5, approvalExpiryMinutes: 15,
}]

const health = {
  overall: 'healthy',
  checks: [{ name: 'application', status: 'healthy', detail: 'API host is running.' }],
  generatedAtUtc: '2026-08-25T09:00:00Z',
}

const dashboard = {
  periodDays: 30,
  sessionsByState: [{ state: 'completed', count: 2 }],
  completedRuns: 2,
  averageDurationSeconds: 12.5,
  approvalCount: 1,
  toolFailureCount: 0,
  providerFailureCount: 0,
  contextCompactionCount: 1,
  estimatedCostUsd: 0.42,
  recentSessions: [session],
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function defaultApiMock() {
  return vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input)
    if (path.includes('/events')) return Promise.resolve(new Response(new ReadableStream(), { headers: { 'Content-Type': 'text/event-stream' } }))
    if (path.endsWith('/health')) return Promise.resolve(jsonResponse(health))
    if (path.startsWith('/api/dashboard')) return Promise.resolve(jsonResponse(dashboard))
    if (path.includes('/workspaces')) return Promise.resolve(jsonResponse([workspace]))
    if (path.includes('/model-profiles')) return Promise.resolve(jsonResponse(modelProfiles))
    if (path.includes('/policy-profiles')) return Promise.resolve(jsonResponse(policyProfiles))
    if (path.includes('/approvals/pending')) return Promise.resolve(jsonResponse([]))
    if (path.endsWith('/changes')) return Promise.resolve(jsonResponse([]))
    if (path.includes('/api/sessions?')) return Promise.resolve(jsonResponse([session]))
    if (path.includes('/api/sessions/')) return Promise.resolve(jsonResponse(session))
    if (init?.method === 'POST') return Promise.resolve(jsonResponse(session))
    return Promise.resolve(jsonResponse([]))
  })
}

function renderPage(element: React.ReactElement) {
  return render(<ThemeProvider><MemoryRouter>{element}</MemoryRouter></ThemeProvider>)
}

describe('dashboard and session chat pages', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders session metrics and activity when the server has sessions', async () => {
    const fetchMock = defaultApiMock()
    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).startsWith('/api/dashboard')) return Promise.resolve(jsonResponse({ ...dashboard, recentSessions: [session, executeSession] }))
      return defaultApiMock()(input, init)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage(<DashboardPage />)

    expect(await screen.findByText('Inspect the parser')).toBeInTheDocument()
    expect(screen.getByText('Completed runs')).toBeInTheDocument()
    expect(screen.getAllByText('Completed')).toHaveLength(4)
  })

  it('renders persisted statistics from the dashboard projection', async () => {
    vi.stubGlobal('fetch', defaultApiMock())
    renderPage(<StatisticsPage />)

    expect(await screen.findByRole('heading', { name: 'Statistics' })).toBeInTheDocument()
    expect(screen.getByText('Completed runs')).toBeInTheDocument()
    expect(screen.getByText('$0.42')).toBeInTheDocument()
    expect(screen.getByText('Sessions by state')).toBeInTheDocument()
  })

  it('opens new sessions directly in the conversation page with in-page controls', async () => {
    vi.stubGlobal('fetch', defaultApiMock())
    renderPage(<NewSessionPage />)

    expect(await screen.findByRole('heading', { level: 1, name: 'Conversation' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Session controls' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Run mode' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Model profile' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Workspace' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Policy and limits' })).toBeInTheDocument()
    expect(screen.queryByText('Task setup')).not.toBeInTheDocument()
  })

  it('creates the session and starts the first run from the conversation composer', async () => {
    const user = userEvent.setup()
    const fetchMock = defaultApiMock()
    vi.stubGlobal('fetch', fetchMock)
    render(<ThemeProvider><MemoryRouter initialEntries={['/sessions/new']}><Routes>
      <Route path="/sessions/new" element={<NewSessionPage />} />
      <Route path="/sessions/:sessionId" element={<div>Chat opened</div>} />
    </Routes></MemoryRouter></ThemeProvider>)

    await screen.findByRole('heading', { level: 1, name: 'Conversation' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Run mode' }), 'execute')
    await user.selectOptions(screen.getByLabelText('Model profile'), 'model_1')
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Find the flaky parser test')
    await user.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions', expect.objectContaining({ method: 'POST' })))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/runs', expect.objectContaining({ method: 'POST' })))
    expect(await screen.findByText('Chat opened')).toBeInTheDocument()
    const init = fetchMock.mock.calls.find(([path]) => path === '/api/sessions')?.[1] as RequestInit | undefined
    expect(init?.body).toEqual(expect.stringContaining('Find the flaky parser test'))
  })

  it('shows a safe start error without exposing provider details', async () => {
    const user = userEvent.setup()
    const fetchMock = defaultApiMock()
    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/runs')) return Promise.resolve(jsonResponse({ detail: 'The selected profile is not eligible.', code: 'profile_ineligible' }, 409))
      return defaultApiMock()(input, init)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage(<NewSessionPage />)
    await screen.findByRole('heading', { level: 1, name: 'Conversation' })
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Try a rejected run')
    await user.click(screen.getByRole('button', { name: 'Send' }))
    expect(await screen.findByText('Chat needs attention')).toBeInTheDocument()
    expect(screen.getByText('The selected profile is not eligible.')).toBeInTheDocument()
  })
})

describe('archive, changes, and administration pages', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('restores an archived session and refreshes the archive list', async () => {
    const user = userEvent.setup()
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') return Promise.resolve(jsonResponse(session))
      if (String(input).includes('/api/sessions?')) return Promise.resolve(jsonResponse([archivedSession]))
      return Promise.resolve(jsonResponse([]))
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage(<ArchivePage />)
    expect(await screen.findByText('Review archived task')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /restore/i }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_archived/restore', expect.objectContaining({ method: 'POST' })))
  })

  it('reports a bounded restore failure', async () => {
    const user = userEvent.setup()
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') return Promise.reject(new Error('restore denied by policy'))
      return Promise.resolve(jsonResponse([archivedSession]))
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage(<ArchivePage />)
    await screen.findByText('Review archived task')
    await user.click(screen.getByRole('button', { name: /restore/i }))
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
  })

  it('renders archive loading failures and the empty archive state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('archive unavailable')))
    renderPage(<ArchivePage />)
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
    cleanup()

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))
    renderPage(<ArchivePage />)
    expect(await screen.findByText('No archived sessions')).toBeInTheDocument()
  })

  it('renders file-level change evidence and an empty-state variant', async () => {
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL) => {
      if (String(input).endsWith('/changes')) return Promise.resolve(jsonResponse([{
        id: 'change_1', runId: 'run_1', status: 'proposed', summary: 'Parser patch',
        createdAtUtc: '2026-08-25T09:10:00Z', files: [
          { path: 'src/parser.ts', changeType: 'modified', binary: false, diffPreview: '+ return value' },
          { path: 'tests/parser.test.ts', changeType: 'added', binary: false, diffPreview: null },
        ],
      }]))
      return Promise.resolve(jsonResponse([]))
    })
    vi.stubGlobal('fetch', fetchMock)
    render(<ThemeProvider><MemoryRouter initialEntries={['/sessions/ses_1/changes']}><Routes><Route path="/sessions/:sessionId/changes" element={<ChangesPage />} /></Routes></MemoryRouter></ThemeProvider>)
    expect(await screen.findByText('Parser patch')).toBeInTheDocument()
    expect(screen.getByText('+ return value')).toBeInTheDocument()
    expect(screen.getByText('No preview supplied.')).toBeInTheDocument()
  })

  it('handles missing session parameters and an empty change response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))
    renderPage(<ChangesPage />)
    expect(await screen.findByText('A session identifier is required.')).toBeInTheDocument()
    cleanup()

    render(<ThemeProvider><MemoryRouter initialEntries={['/sessions/ses_1/changes']}><Routes><Route path="/sessions/:sessionId/changes" element={<ChangesPage />} /></Routes></MemoryRouter></ThemeProvider>)
    expect(await screen.findByText('No changes recorded')).toBeInTheDocument()
  })

  it('registers a workspace and renders model, policy, runtime, and appearance views', async () => {
    const user = userEvent.setup()
    const fetchMock = defaultApiMock()
    vi.stubGlobal('fetch', fetchMock)
    renderPage(<WorkspacesSettingsPage />)
    await screen.findByText('SharpAgent / storefront')
    await user.type(screen.getByLabelText('Workspace name'), 'New workspace')
    await user.type(screen.getByLabelText('Workspace root path'), 'C:\\work\\new')
    await user.click(screen.getByRole('button', { name: /register workspace/i }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/workspaces', expect.objectContaining({ method: 'POST' })))

    renderPage(<ModelsSettingsPage />)
    expect(await screen.findByText('DeepSeek Coder')).toBeInTheDocument()
    expect(screen.getByText('Plan only')).toBeInTheDocument()

    renderPage(<PolicySettingsPage />)
    expect(await screen.findByText('Balanced local policy')).toBeInTheDocument()
    expect(screen.getByText('$2.50')).toBeInTheDocument()

    renderPage(<RuntimeSettingsPage />)
    expect(await screen.findByLabelText('Service health')).toBeInTheDocument()

    renderPage(<AppearanceSettingsPage />)
    await user.click(screen.getByRole('button', { name: /ocean/i }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'ocean')
  })

  it('surfaces a workspace registration failure', async () => {
    const user = userEvent.setup()
    const fetchMock = defaultApiMock()
    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') return Promise.reject(new Error('workspace rejected'))
      return defaultApiMock()(input, init)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage(<WorkspacesSettingsPage />)
    await screen.findByText('SharpAgent / storefront')
    await user.type(screen.getByLabelText('Workspace name'), 'Rejected workspace')
    await user.type(screen.getByLabelText('Workspace root path'), 'C:\\work\\rejected')
    await user.click(screen.getByRole('button', { name: /register workspace/i }))
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
  })

  it('shows a settings error state when the catalog cannot be reached', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('catalog offline')))
    renderPage(<ModelsSettingsPage />)
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('renders the bounded runtime error projection', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('runtime offline')))
    renderPage(<RuntimeSettingsPage />)
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
  })

  it('keeps new-session setup safe when the catalog is unavailable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('catalog offline')))
    renderPage(<NewSessionPage />)
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
  })
})
