import { createBrowserRouter, type RouteObject } from 'react-router'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { PlaceholderPage } from './placeholder-page'
import { RootLayout } from './root-layout'

const PENDING_ROUTES = [
  { path: '/sessions/new', title: 'New task' },
  { path: '/sessions/archive', title: 'Archived sessions' },
  { path: '/sessions/:sessionId', title: 'Session workspace' },
  { path: '/sessions/:sessionId/changes', title: 'Changes' },
  { path: '/settings/workspaces', title: 'Workspace settings' },
  { path: '/settings/models', title: 'Model profiles' },
  { path: '/settings/policy', title: 'Policy and limits' },
  { path: '/settings/runtime', title: 'Runtime health' },
  { path: '/settings/appearance', title: 'Appearance' },
] as const

/** Route objects shared by the browser router and tests (memory router). */
export function createAppRoutes(): RouteObject[] {
  return [
    {
      element: <RootLayout />,
      children: [
        { path: '/', element: <DashboardPage /> },
        ...PENDING_ROUTES.map((route) => ({
          path: route.path,
          element: <PlaceholderPage title={route.title} />,
        })),
      ],
    },
  ]
}

/** Application router; see functional specification section 9.1 for required routes. */
export function createAppRouter() {
  return createBrowserRouter(createAppRoutes())
}
