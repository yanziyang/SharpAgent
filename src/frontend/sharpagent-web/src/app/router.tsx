import { createBrowserRouter, type RouteObject } from 'react-router'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { StatisticsPage } from '@/features/dashboard/statistics-page'
import { ArchivePage } from '@/features/sessions/archive-page'
import { ChangesPage } from '@/features/sessions/changes-page'
import { NewSessionPage } from '@/features/sessions/new-session-page'
import { SessionWorkspacePage } from '@/features/sessions/session-workspace-page'
import {
  AppearanceSettingsPage,
  ModelsSettingsPage,
  PolicySettingsPage,
  RuntimeSettingsPage,
  WorkspacesSettingsPage,
} from '@/features/settings/settings-pages'
import { RootLayout } from './root-layout'

/** Route objects shared by the browser router and tests (memory router). */
export function createAppRoutes(): RouteObject[] {
  return [
    {
      element: <RootLayout />,
      children: [
        { path: '/', element: <DashboardPage /> },
        { path: '/statistics', element: <StatisticsPage /> },
        { path: '/sessions/new', element: <NewSessionPage /> },
        { path: '/sessions/archive', element: <ArchivePage /> },
        { path: '/sessions/:sessionId', element: <SessionWorkspacePage /> },
        { path: '/sessions/:sessionId/changes', element: <ChangesPage /> },
        { path: '/settings/workspaces', element: <WorkspacesSettingsPage /> },
        { path: '/settings/models', element: <ModelsSettingsPage /> },
        { path: '/settings/policy', element: <PolicySettingsPage /> },
        { path: '/settings/runtime', element: <RuntimeSettingsPage /> },
        { path: '/settings/appearance', element: <AppearanceSettingsPage /> },
      ],
    },
  ]
}

/** Application router; see functional specification section 9.1 for required routes. */
export function createAppRouter() {
  return createBrowserRouter(createAppRoutes())
}
