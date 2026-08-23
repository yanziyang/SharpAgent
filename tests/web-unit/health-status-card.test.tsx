import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HealthStatusCard } from '@/features/health/health-status-card'
import { healthStatusLabel } from '@/features/health/health-status-labels'
import type { HealthSnapshot } from '@/shared/api/client'

const snapshot: HealthSnapshot = {
  overall: 'degraded',
  checks: [
    { name: 'application', status: 'healthy', detail: 'API host is running.' },
    { name: 'database', status: 'degraded', detail: 'SQLite persistence is not configured yet.' },
  ],
  generatedAtUtc: '2026-08-23T10:00:00Z',
}

describe('HealthStatusCard', () => {
  it('renders overall status and every dependency check as text, not color only', () => {
    render(<HealthStatusCard snapshot={snapshot} />)

    const region = screen.getByLabelText('Service health')
    expect(region).toBeInTheDocument()
    // Overall degraded plus the database check: status is always readable text.
    expect(screen.getAllByText('Degraded')).toHaveLength(2)
    expect(screen.getByText('application')).toBeInTheDocument()
    expect(screen.getByText('Healthy')).toBeInTheDocument()
    expect(screen.getByText('database')).toBeInTheDocument()
    expect(screen.getByText(/SQLite persistence is not configured yet\./)).toBeInTheDocument()
  })

  it('omits the detail line when a check has none', () => {
    render(
      <HealthStatusCard
        snapshot={{
          overall: 'healthy',
          checks: [{ name: 'application', status: 'healthy', detail: null }],
          generatedAtUtc: '2026-08-23T10:00:00Z',
        }}
      />,
    )

    expect(screen.getByText('application')).toBeInTheDocument()
    expect(screen.queryByText(/running/i)).not.toBeInTheDocument()
  })

  it('labels all supported statuses for non-color signaling', () => {
    expect(healthStatusLabel('healthy')).toBe('Healthy')
    expect(healthStatusLabel('degraded')).toBe('Degraded')
    expect(healthStatusLabel('unready')).toBe('Unready')
  })
})
