import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RootLayout } from '@/app/root-layout'
import { SessionWorkspacePage } from '@/features/sessions/session-workspace-page'
import { ThemeProvider } from '@/shared/theme/theme-provider'

const summary = {
  id: 'ses_1', task: 'Inspect the parser', mode: 'plan', status: 'executing', workspaceId: 'ws_1',
  modelProfileId: 'model_1', activeRunId: 'run_1', archived: false,
  createdAtUtc: '2026-08-25T09:00:00Z', updatedAtUtc: '2026-08-25T09:10:00Z',
}

const session = {
  ...summary,
  policyProfileId: 'policy_1',
  runs: [{ id: 'run_1', sequence: 1, status: 'executing', startedAtUtc: '2026-08-25T09:00:00Z', endedAtUtc: null, stopReason: null, resumeSourceRunId: null }],
}

const approval = {
  id: 'approval_1', runId: 'run_1', sessionId: 'ses_1', actionType: 'apply_patch',
  summary: 'Apply the focused parser patch.', affectedPaths: ['src/parser.ts', 'tests/parser.test.ts'],
  status: 'pending', expiresAtUtc: '2026-08-25T10:00:00Z',
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function catalogResponse(path: string): Response | undefined {
  if (path.endsWith('/workspaces')) return jsonResponse([{ id: 'ws_1', name: 'SharpAgent repository' }])
  if (path.endsWith('/model-profiles')) return jsonResponse([{ id: 'model_1', displayName: 'Ox Alpha Free' }])
  if (path.endsWith('/policy-profiles')) return jsonResponse([{ id: 'policy_1', name: 'Default safe policy' }])
  return undefined
}

function renderRoot(initialEntry: string, fetchMock: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetchMock)
  const router = createMemoryRouter([
    { path: '*', element: <RootLayout />, children: [{ path: '*', element: <div /> }] },
  ], { initialEntries: [initialEntry] })
  return render(<ThemeProvider><RouterProvider router={router} /></ThemeProvider>)
}

describe('prototype shell', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('supports navigation collapse, mobile menu, active session context, and administration links', async () => {
    const user = userEvent.setup()
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([summary]))
    renderRoot('/sessions/ses_1', fetchMock)

    expect((await screen.findAllByText('Inspect the parser')).length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('Trusted local workspace')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Agent' })).toHaveAttribute('href', '/sessions/ses_1')

    await user.click(screen.getByRole('button', { name: 'Collapse navigation' }))
    expect(document.querySelector('.app-shell')).toHaveClass('sidebar-collapsed')
    await user.click(screen.getByRole('button', { name: 'Expand navigation' }))
    await user.click(screen.getByRole('button', { name: 'Toggle navigation' }))
    expect(document.querySelector('.app-shell')).toHaveClass('mobile-sidebar-open')
    expect(screen.getByRole('dialog')).toHaveTextContent('Navigation')
    await user.click(screen.getByRole('button', { name: 'Close mobile navigation' }))
    expect(document.querySelector('.app-shell')).not.toHaveClass('mobile-sidebar-open')

    await user.click(screen.getByRole('button', { name: 'New session' }))
    await user.click(screen.getByRole('button', { name: 'Open administration' }))
    await user.click(screen.getByRole('button', { name: 'Create session' }))

    await user.click(screen.getByRole('link', { name: 'Runtime' }))
    expect(screen.getByRole('link', { name: 'Runtime' })).toHaveAttribute('aria-current', 'page')
  })

  it('keeps the shell usable when the recent-session list fails', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error('session list unavailable'))
    renderRoot('/sessions/ses_1', fetchMock)
    expect(await screen.findByText('No sessions yet')).toBeInTheDocument()
  })
})

function streamResponse(controllerRef: { current?: ReadableStreamDefaultController<Uint8Array> }): Response {
  return new Response(new ReadableStream<Uint8Array>({
    start(controller) {
      controllerRef.current = controller
    },
  }), { headers: { 'Content-Type': 'text/event-stream' } })
}

function renderWorkspace(fetchMock: ReturnType<typeof vi.fn>) {
  const router = createMemoryRouter([
    { path: '/sessions/:sessionId', element: <SessionWorkspacePage /> },
    { path: '/sessions/archive', element: <div>Archived conversations</div> },
  ], { initialEntries: ['/sessions/ses_1'] })
  vi.stubGlobal('fetch', fetchMock)
  return render(<ThemeProvider><RouterProvider router={router} /></ThemeProvider>)
}

describe('session chat', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders only conversation messages while assistant output streams', async () => {
    const controllerRef: { current?: ReadableStreamDefaultController<Uint8Array> } = {}
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL) => {
      const path = String(input)
      const catalog = catalogResponse(path)
      if (catalog) return Promise.resolve(catalog)
      if (path.endsWith('/events')) return Promise.resolve(streamResponse(controllerRef))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      return Promise.resolve(jsonResponse(session))
    })
    renderWorkspace(fetchMock)

    expect(await screen.findByRole('heading', { level: 1, name: 'Conversation' })).toBeInTheDocument()
    await waitFor(() => expect(controllerRef.current).toBeDefined())
    actEnqueue(controllerRef.current, JSON.stringify({
      sequence: 1, type: 'run_started', sessionId: 'ses_1', runId: 'run_1', eventId: 'evt_1',
      occurredAtUtc: '2026-08-25T09:05:00Z', payload: { instruction: null },
    }), 'run_started', '1')
    actEnqueue(controllerRef.current, JSON.stringify({
      sequence: 2, type: 'tool_completed', sessionId: 'ses_1', runId: 'run_1', eventId: 'evt_2',
      occurredAtUtc: '2026-08-25T09:05:01Z', payload: { tool: 'read_file' },
    }), 'tool_completed', '2')
    actEnqueue(controllerRef.current, JSON.stringify({
      sequence: 3, type: 'assistant_summary', sessionId: 'ses_1', runId: 'run_1', eventId: 'evt_3',
      occurredAtUtc: '2026-08-25T09:05:02Z', payload: { summary: 'Beijing is the ' },
    }), 'assistant_summary', '3')
    actEnqueue(controllerRef.current, JSON.stringify({
      sequence: 4, type: 'assistant_summary', sessionId: 'ses_1', runId: 'run_1', eventId: 'evt_4',
      occurredAtUtc: '2026-08-25T09:05:03Z', payload: { summary: 'capital of China.' },
    }), 'assistant_summary', '4')

    expect(await screen.findByText('Beijing is the capital of China.', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('Streaming')).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Changes' })).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Terminal' })).not.toBeInTheDocument()
    expect(screen.queryByRole('complementary', { name: 'Session details' })).not.toBeInTheDocument()
    expect(screen.queryByText('read_file')).not.toBeInTheDocument()
  })

  it('sends a follow-up message through the chat composer', async () => {
    const completedSession = {
      ...session,
      status: 'completed',
      activeRunId: null,
      runs: [{ ...session.runs[0], status: 'completed', endedAtUtc: '2026-08-25T09:10:00Z' }],
    }
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      const catalog = catalogResponse(path)
      if (catalog) return Promise.resolve(catalog)
      if (path.endsWith('/events')) return Promise.resolve(new Response(new ReadableStream(), { headers: { 'Content-Type': 'text/event-stream' } }))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      if (path.endsWith('/runs') && init?.method === 'POST') return Promise.resolve(jsonResponse({ session: completedSession, run: completedSession.runs[0] }))
      return Promise.resolve(jsonResponse(completedSession))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)

    await screen.findByRole('heading', { level: 1, name: 'Conversation' })
    const composer = screen.getByRole('textbox', { name: 'Send a follow-up message' })
    await user.type(composer, 'Continue with the focused test')
    await user.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/runs', expect.objectContaining({
      method: 'POST',
      body: expect.stringContaining('Continue with the focused test'),
    })))
  })

  it('keeps approval controls visible in the conversation', async () => {
    const controllerRef: { current?: ReadableStreamDefaultController<Uint8Array> } = {}
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      const catalog = catalogResponse(path)
      if (catalog) return Promise.resolve(catalog)
      if (path.endsWith('/events')) return Promise.resolve(streamResponse(controllerRef))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([approval]))
      if (init?.method === 'POST') return Promise.resolve(jsonResponse({}))
      return Promise.resolve(jsonResponse(session))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)

    expect(await screen.findByText('Apply the focused parser patch.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Approve once' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/approvals/approval_1/resolve', expect.objectContaining({ method: 'POST' })))
  })

  it('archives an inactive conversation behind a confirmation', async () => {
    const completedSession = {
      ...session,
      status: 'completed',
      activeRunId: null,
      runs: [{ ...session.runs[0], status: 'completed', endedAtUtc: '2026-08-25T09:10:00Z' }],
    }
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      const catalog = catalogResponse(path)
      if (catalog) return Promise.resolve(catalog)
      if (path.endsWith('/events')) return Promise.resolve(new Response(new ReadableStream(), { headers: { 'Content-Type': 'text/event-stream' } }))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      if (path.endsWith('/archive') && init?.method === 'POST') return Promise.resolve(jsonResponse(completedSession))
      return Promise.resolve(jsonResponse(completedSession))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)

    await screen.findByRole('heading', { level: 1, name: 'Conversation' })
    await user.click(screen.getByRole('button', { name: 'Archive' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Archive this conversation?')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/archive', expect.objectContaining({ method: 'POST' })))
    expect(await screen.findByText('Archived conversations')).toBeInTheDocument()
  })

  it('shows a stream interruption and command error for an active response', async () => {
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      const catalog = catalogResponse(path)
      if (catalog) return Promise.resolve(catalog)
      if (path.endsWith('/events')) return Promise.reject(new Error('stream unavailable'))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      if (path.endsWith('/cancel') && init?.method === 'POST') return Promise.reject(new Error('cancel unavailable'))
      return Promise.resolve(jsonResponse(session))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)

    await screen.findByRole('heading', { level: 1, name: 'Conversation' })
    expect(await screen.findByText('stream unavailable')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Stop response' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Stop this response?')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    expect(await screen.findByText('The SharpAgent service is unreachable.')).toBeInTheDocument()
  })
})

function actEnqueue(controller: ReadableStreamDefaultController<Uint8Array> | undefined, payload: string, type: string, id: string) {
  act(() => controller?.enqueue(new TextEncoder().encode(`id: ${id}\nevent: ${type}\ndata: ${payload}\n\n`)))
}
