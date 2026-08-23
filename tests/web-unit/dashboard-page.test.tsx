import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { ThemeProvider } from '@/shared/theme/theme-provider'

function renderDashboard(): void {
  render(
    <ThemeProvider>
      <DashboardPage />
    </ThemeProvider>,
  )
}

describe('DashboardPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows a loading status first, then the health projection', async () => {
    let resolveHealth!: (response: Response) => void
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(
        () =>
          new Promise<Response>((resolve) => {
            resolveHealth = resolve
          }),
      ),
    )

    renderDashboard()

    expect(screen.getByRole('status')).toHaveTextContent(/loading service health/i)

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
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fetch failed')))

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('Health unavailable')).toBeInTheDocument()
    })
    expect(screen.getByText(/service is unreachable/i)).toBeInTheDocument()
  })

  it('invites creating the first session when none exist', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            overall: 'healthy',
            checks: [],
            generatedAtUtc: '2026-08-23T10:00:00Z',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      ),
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/No sessions yet/i)).toBeInTheDocument()
    })
  })
})
