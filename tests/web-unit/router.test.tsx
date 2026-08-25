import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { createAppRouter, createAppRoutes } from '@/app/router'
import { ThemeProvider } from '@/shared/theme/theme-provider'

function renderRoute(path: string): void {
  const router = createMemoryRouter(createAppRoutes(), { initialEntries: [path] })
  render(
    <ThemeProvider>
      <RouterProvider router={router} />
    </ThemeProvider>,
  )
}

describe('application routes', () => {
  it.each([
    ['/statistics', 'Statistics'],
    ['/sessions/new', 'New session'],
    ['/sessions/archive', 'Archived sessions'],
    ['/sessions/ses_123', 'Session workspace'],
    ['/sessions/ses_123/changes', 'Changes'],
    ['/settings/workspaces', 'Workspace settings'],
    ['/settings/models', 'Model profiles'],
    ['/settings/policy', 'Policy and limits'],
    ['/settings/runtime', 'Runtime health'],
    ['/settings/appearance', 'Appearance'],
  ])('renders the required route %s', (path, title) => {
    renderRoute(path)

    expect(screen.getByRole('heading', { level: 1, name: title })).toBeInTheDocument()
  })

  it('renders primary navigation for every planned area', async () => {
    renderRoute('/')

    for (const label of ['Dashboard', 'Statistics', 'New session', 'Archive', 'Workspaces', 'Models', 'Policy', 'Runtime', 'Appearance']) {
      expect(screen.getByRole('link', { name: label })).toBeInTheDocument()
    }
  })

  it('navigates from the dashboard to a settings page via the nav rail', async () => {
    const user = userEvent.setup()

    renderRoute('/')

    await user.click(screen.getByRole('link', { name: 'Models' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Model profiles' })).toBeInTheDocument()
  })

  it('shows the trusted-local deployment notice', () => {
    renderRoute('/')

    expect(screen.getByRole('contentinfo')).toHaveTextContent(/no authentication by design/i)
  })

  it('constructs the browser router from the same route table', () => {
    expect(createAppRouter()).toBeDefined()
  })
})
