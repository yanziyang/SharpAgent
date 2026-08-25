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
  runs: [{ id: 'run_1', sequence: 1, status: 'executing', startedAtUtc: '2026-08-25T09:00:00Z', endedAtUtc: null, stopReason: null, resumeSourceRunId: null }],
}

const approval = {
  id: 'approval_1', runId: 'run_1', sessionId: 'ses_1', actionType: 'apply_patch',
  summary: 'Apply the focused parser patch.', affectedPaths: ['src/parser.ts', 'tests/parser.test.ts'],
  status: 'pending', expiresAtUtc: '2026-08-25T10:00:00Z',
}

const singlePathApproval = { ...approval, id: 'approval_single', affectedPaths: ['src/parser.ts'] }
const emptyPathApproval = { ...approval, id: 'approval_empty', affectedPaths: [] }

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
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
  ], { initialEntries: ['/sessions/ses_1'] })
  vi.stubGlobal('fetch', fetchMock)
  return render(<ThemeProvider><RouterProvider router={router} /></ThemeProvider>)
}

describe('session workspace', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders timeline, approval details, tabs, composer, and run controls', async () => {
    const controllerRef: { current?: ReadableStreamDefaultController<Uint8Array> } = {}
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      if (path.endsWith('/events')) return Promise.resolve(streamResponse(controllerRef))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([approval, singlePathApproval, emptyPathApproval]))
      if (path.endsWith('/api/sessions/ses_1')) return Promise.resolve(jsonResponse(session))
      if (init?.method === 'POST') return Promise.resolve(jsonResponse(session))
      return Promise.resolve(jsonResponse(session))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)

    expect(await screen.findByRole('heading', { level: 1, name: 'Inspect the parser' })).toBeInTheDocument()
    await waitFor(() => expect(controllerRef.current).toBeDefined())
    actEnqueue(controllerRef.current, JSON.stringify({
      sequence: 1, type: 'approval_requested', sessionId: 'ses_1', runId: 'run_1', eventId: 'evt_1',
      occurredAtUtc: '2026-08-25T09:05:00Z', payload: { approvalId: 'approval_1', summary: 'Permission required' },
    }), 'approval_requested', '1')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 2, payload: { message: 'Todo created' } }), 'todo_created', '2')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 3, payload: { tool: 'read_file' } }), 'tool_completed', '3')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 4, payload: { path: 'src/parser.ts' } }), 'change_detected', '4')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 5, payload: { detail: 'Context compacted' } }), 'context_compacted', '5')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 6, payload: { output: 'validated' } }), 'run_completed', '6')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 7, payload: {} }), 'unknown_event', '7')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 8, payload: { approvalId: 'approval_single' } }), 'approval_requested', '8')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 9, payload: { approvalId: 'approval_empty' } }), 'approval_requested', '9')
    actEnqueue(controllerRef.current, JSON.stringify({ sequence: 10, payload: { message: 'Todo completed' } }), 'todo_updated', '10')
    expect((await screen.findAllByText('Apply the focused parser patch.')).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('2 bounded paths')).toBeInTheDocument()
    expect(screen.getByText('1 bounded path')).toBeInTheDocument()
    expect(screen.getByText('No file paths disclosed')).toBeInTheDocument()
    expect(screen.getAllByText('Todo completed').length).toBeGreaterThanOrEqual(2)

    await user.click(screen.getAllByRole('button', { name: 'Approve once' })[0]!)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/approvals/approval_1/resolve', expect.objectContaining({ method: 'POST' })))

    await user.click(screen.getByRole('tab', { name: 'Changes' }))
    expect(screen.getByRole('heading', { name: 'Change review' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Terminal' }))
    expect(screen.getByRole('heading', { name: 'Bounded terminal evidence' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Final review' }))
    expect(screen.getByRole('heading', { name: 'Run controls' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Cancel active run' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Cancel this run?')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/cancel', expect.objectContaining({ method: 'POST' })))
    await user.click(screen.getByRole('button', { name: 'Details' }))
    expect(screen.queryByRole('complementary', { name: 'Session details' })).not.toBeInTheDocument()
  })

  it('starts a follow-up and exposes safe stream failures', async () => {
    const completedSession = { ...session, status: 'completed', activeRunId: null, runs: [{ ...session.runs[0], status: 'completed', endedAtUtc: '2026-08-25T09:10:00Z' }] }
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      if (path.endsWith('/events')) return Promise.reject(new Error('stream unavailable'))
      if (init?.method === 'POST') return Promise.resolve(jsonResponse(completedSession))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      return Promise.resolve(jsonResponse(completedSession))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)
    expect(await screen.findByRole('heading', { level: 1, name: 'Inspect the parser' })).toBeInTheDocument()
    const composer = screen.getByRole('textbox', { name: 'Send a follow-up instruction' })
    await user.type(composer, 'Continue with the focused test')
    await user.click(screen.getByRole('button', { name: 'Resume run' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/runs', expect.objectContaining({ method: 'POST' })))
  })

  it('archives an inactive session behind a confirmation dialog', async () => {
    const completedSession = { ...session, status: 'completed', activeRunId: null, runs: [{ ...session.runs[0], status: 'completed', endedAtUtc: '2026-08-25T09:10:00Z' }] }
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      if (path.endsWith('/events')) return Promise.resolve(new Response(new ReadableStream(), { headers: { 'Content-Type': 'text/event-stream' } }))
      if (init?.method === 'POST') return Promise.resolve(jsonResponse(completedSession))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      return Promise.resolve(jsonResponse(completedSession))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)
    await screen.findByRole('heading', { level: 1, name: 'Inspect the parser' })
    await user.click(screen.getByRole('tab', { name: 'Final review' }))
    await user.click(screen.getByRole('button', { name: 'Archive session' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Archive this session?')
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/archive', expect.objectContaining({ method: 'POST' })))
  })

  it('renders draft execute controls and starts with an empty instruction', async () => {
    const draftSession = { ...session, status: 'draft', mode: 'execute', activeRunId: null, runs: [] }
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      if (path.endsWith('/events')) return Promise.resolve(new Response(new ReadableStream(), { headers: { 'Content-Type': 'text/event-stream' } }))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      if (init?.method === 'POST') return Promise.resolve(jsonResponse(draftSession))
      return Promise.resolve(jsonResponse(draftSession))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)
    await screen.findByRole('heading', { level: 1, name: 'Inspect the parser' })
    expect(screen.getAllByText('Controlled execute').length).toBeGreaterThanOrEqual(2)
    expect(screen.getByText('Not started')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Start run' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sessions/ses_1/runs', expect.objectContaining({ method: 'POST' })))
  })

  it('shows a stream interruption and command error for an active run', async () => {
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      if (path.endsWith('/events')) return Promise.reject(new Error('stream unavailable'))
      if (path.endsWith('/approvals/pending')) return Promise.resolve(jsonResponse([]))
      if (path.endsWith('/cancel')) return Promise.reject(new Error('cancel unavailable'))
      return Promise.resolve(jsonResponse(session))
    })
    const user = userEvent.setup()
    renderWorkspace(fetchMock)
    await screen.findByRole('heading', { level: 1, name: 'Inspect the parser' })
    expect(await screen.findByText('stream unavailable')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Final review' }))
    await user.click(screen.getByRole('button', { name: 'Cancel active run' }))
    await user.click(screen.getByRole('button', { name: 'Confirm' }))
    expect(await screen.findByText('Command needs attention')).toBeInTheDocument()
  })
})

function actEnqueue(controller: ReadableStreamDefaultController<Uint8Array> | undefined, payload: string, type: string, id: string) {
  act(() => controller?.enqueue(new TextEncoder().encode(`id: ${id}\nevent: ${type}\ndata: ${payload}\n\n`)))
}
