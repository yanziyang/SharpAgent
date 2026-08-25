import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { ThemeProvider } from '@/shared/theme/theme-provider'

function renderDashboard(): void {
  render(
    <ThemeProvider>
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>
    </ThemeProvider>,
  )
}

const dashboard = {
  periodDays: 30,
  sessionsByState: [{ state: 'completed', count: 1 }],
  completedRuns: 1,
  averageDurationSeconds: 12.5,
  approvalCount: 0,
  toolFailureCount: 0,
  providerFailureCount: 0,
  contextCompactionCount: 0,
  estimatedCostUsd: 0.12,
  recentSessions: [],
}

describe('DashboardPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows a loading status first, then the health projection', async () => {
    let resolveHealth!: (response: Response) => void
    vi.stubGlobal('fetch', vi.fn().mockImplementation((input: RequestInfo | URL) => {
      if (String(input).startsWith('/api/health')) {
        return new Promise<Response>((resolve) => {
          resolveHealth = resolve
        })
      }

      if (String(input).startsWith('/api/dashboard')) {
        return Promise.resolve(new Response(JSON.stringify(dashboard), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      }

      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    }))

    renderDashboard()

    expect(screen.getByText(/loading service health/i)).toBeInTheDocument()

    resolveHealth(
      new Response(
        JSON.stringify({
          overall: 'healthy',
          checks: [{ name: 'application', status: 'healthy', detail: 'API host is running.' }],
          generatedAtUtc: '2026-08-23T10:00:00Z',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )

    await waitFor(() => {
      expect(screen.getByLabelText('Service health')).toBeInTheDocument()
    })
    expect(screen.getAllByText('Healthy').length).toBe(2)
  })

  it('renders a safe error card when the API is unreachable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((input: RequestInfo | URL) => {
      if (String(input).startsWith('/api/health')) {
        return Promise.reject(new TypeError('fetch failed'))
      }

      if (String(input).startsWith('/api/dashboard')) {
        return Promise.resolve(new Response(JSON.stringify(dashboard), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      }

      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    }))

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('Health unavailable')).toBeInTheDocument()
    })
    expect(screen.getByText(/service is unreachable/i)).toBeInTheDocument()
  })

  it('invites creating the first session when none exist', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((input: RequestInfo | URL) => {
      if (String(input).startsWith('/api/health')) {
        return Promise.resolve(new Response(JSON.stringify({
          overall: 'healthy',
          checks: [],
          generatedAtUtc: '2026-08-23T10:00:00Z',
        }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      }

      if (String(input).startsWith('/api/dashboard')) {
        return Promise.resolve(new Response(JSON.stringify(dashboard), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      }

      return Promise.resolve(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    }))

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/No sessions yet/i)).toBeInTheDocument()
    })
  })
})
