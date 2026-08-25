import { useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import {
  Archive,
  BarChart3,
  ChevronLeft,
  ChevronRight,
  CirclePlus,
  Command,
  FolderKanban,
  Home,
  Menu,
  PanelRight,
  Palette,
  Settings2,
  Sparkles,
  X,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { StatusBadge } from '@/components/status-badge'
import { useSessionList } from '@/features/sessions/use-session-data'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import type { SessionSummary } from '@/shared/api/client'
import type { ResourceState } from '@/shared/api/use-resource'
import { cn } from '@/lib/utils'
import { ThemeMenu } from '@/shared/theme/theme-menu'

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard', end: true, icon: Home },
  { to: '/statistics', label: 'Statistics', icon: BarChart3 },
  { to: '/sessions/new', label: 'New session', icon: CirclePlus },
  { to: '/sessions/archive', label: 'Archive', icon: Archive },
  { to: '/settings/workspaces', label: 'Workspaces', icon: FolderKanban },
  { to: '/settings/models', label: 'Models', icon: Sparkles },
  { to: '/settings/policy', label: 'Policy', icon: Settings2 },
  { to: '/settings/runtime', label: 'Runtime', icon: PanelRight },
  { to: '/settings/appearance', label: 'Appearance', icon: Palette },
]

type SessionListState = ResourceState<SessionSummary[]> & { reload: () => void }

function SidebarNavigation({
  sessions,
  activeSessionId,
  closeNavigation,
}: {
  sessions: SessionListState
  activeSessionId: string | undefined
  closeNavigation: () => void
}) {
  const navigate = useNavigate()

  return (
    <div className="sidebar-scroll">
      <div className="mode-switch" role="tablist" aria-label="Primary location">
        <NavLink to="/" role="tab" end className={({ isActive }) => cn('mode-switch-item', isActive && 'active')} onClick={closeNavigation}>
          <Home data-icon="inline-start" />
          <span>Home</span>
        </NavLink>
        <NavLink to={activeSessionId ? `/sessions/${activeSessionId}` : '/sessions/new'} role="tab" className={({ isActive }) => cn('mode-switch-item', isActive && 'active')} onClick={closeNavigation}>
          <Sparkles data-icon="inline-start" />
          <span>Agent</span>
        </NavLink>
      </div>

      <nav className="side-nav" aria-label="Main navigation">
        {NAV_ITEMS.slice(0, 4).map((item) => {
          const Icon = item.icon
          return (
            <NavLink key={item.to} to={item.to} end={'end' in item ? item.end : false} className={({ isActive }) => cn('side-button', isActive && 'active', item.to === '/sessions/new' && 'new')} onClick={closeNavigation}>
              <Icon data-icon="inline-start" />
              <span className="side-label">{item.label}</span>
            </NavLink>
          )
        })}
      </nav>

      <div className="nav-label"><span>Workspace</span><Button aria-label="Create session" variant="ghost" size="icon-xs" onClick={() => { navigate('/sessions/new'); closeNavigation() }}><CirclePlus data-icon="inline-start" /></Button></div>
      <div className="project-heading"><span aria-hidden className="project-pulse" />SharpAgent / trusted local</div>
      <div className="session-list">
        {sessions.kind === 'ready' && Array.isArray(sessions.data) && sessions.data.length > 0 ? sessions.data.slice(0, 6).map((session) => (
          <NavLink key={session.id} to={`/sessions/${session.id}`} className={({ isActive }) => cn('session-link', isActive && 'active')} onClick={closeNavigation}>
            <span aria-hidden className={cn('session-status', `session-status-${session.status}`)} />
            <span className="session-name">{session.task}</span>
            <StatusBadge status={session.status} className="session-status-badge" />
          </NavLink>
        )) : <p className="sidebar-empty">No sessions yet</p>}
      </div>

      <div className="sidebar-settings">
        <span className="nav-label">Administration</span>
        {NAV_ITEMS.slice(4).map((item) => {
          const Icon = item.icon
          return <NavLink key={item.to} to={item.to} className={({ isActive }) => cn('side-button', isActive && 'active')} onClick={closeNavigation}><Icon data-icon="inline-start" /><span className="side-label">{item.label}</span></NavLink>
        })}
      </div>
    </div>
  )
}

export function RootLayout() {
  const navigate = useNavigate()
  const location = useLocation()
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false)
  const sessions = useSessionList(false, location.pathname.startsWith('/sessions/'))
  const activeSessionId = location.pathname.match(/^\/sessions\/([^/]+)/)?.[1]
  const activeSession = sessions.kind === 'ready' && Array.isArray(sessions.data)
    ? sessions.data.find((session) => session.id === activeSessionId)
    : undefined

  const closeMobileSidebar = () => setMobileSidebarOpen(false)

  return (
    <Sheet open={mobileSidebarOpen} onOpenChange={setMobileSidebarOpen}>
      <div className={cn('app-shell', sidebarCollapsed && 'sidebar-collapsed', mobileSidebarOpen && 'mobile-sidebar-open')}>
        <header className="web-header">
          <SheetTrigger render={<Button aria-label="Toggle navigation" className="mobile-menu-button" variant="ghost" size="icon" />}>
            {mobileSidebarOpen ? <X data-icon="inline-start" /> : <Menu data-icon="inline-start" />}
          </SheetTrigger>
        <Button
          aria-label={sidebarCollapsed ? 'Expand navigation' : 'Collapse navigation'}
          className="desktop-collapse-button"
          variant="ghost"
          size="icon"
          onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
        >
          {sidebarCollapsed ? <ChevronRight data-icon="inline-start" /> : <ChevronLeft data-icon="inline-start" />}
        </Button>
        <Link to="/" className="header-brand" onClick={closeMobileSidebar}>
          <span aria-hidden className="brand-mark">
            <Sparkles />
          </span>
          <span className="brand-copy">
            <span className="brand-name">SharpAgent</span>
            <span className="brand-subtitle">Controlled AI coding workspace</span>
          </span>
        </Link>
        <div className="header-context">
          <span>Active session</span>
          <strong>{activeSession?.task ?? 'Ready for a controlled task'}</strong>
        </div>
        <div className="header-spacer" />
        <div className="header-actions">
          <span className="header-status"><span aria-hidden className="status-dot status-dot-completed" />Trusted local workspace</span>
          <Button onClick={() => navigate('/sessions/new')}>
            <CirclePlus data-icon="inline-start" />
            New session
          </Button>
          <Button aria-label="Open command palette" title="Command palette" variant="ghost" size="icon">
            <Command data-icon="inline-start" />
          </Button>
          <Button aria-label="Open administration" title="Administration" variant="ghost" size="icon" onClick={() => navigate('/settings/runtime')}>
            <Settings2 data-icon="inline-start" />
          </Button>
          <ThemeMenu />
        </div>
        </header>

        <SheetContent side="left" className="mobile-navigation-sheet" closeLabel="Close mobile navigation">
          <SheetHeader>
            <SheetTitle>Navigation</SheetTitle>
            <SheetDescription>Move between the trusted-local workspace areas.</SheetDescription>
          </SheetHeader>
          <SidebarNavigation sessions={sessions} activeSessionId={activeSessionId} closeNavigation={closeMobileSidebar} />
        </SheetContent>

        <aside className="app-sidebar" aria-label="Application navigation">
          <SidebarNavigation sessions={sessions} activeSessionId={activeSessionId} closeNavigation={closeMobileSidebar} />
        <div className="sidebar-footer">
          <span className="avatar">SA</span>
          <span className="profile-copy"><strong>Local operator</strong><small>Trusted deployment</small></span>
        </div>
        </aside>

        <main className="app-main">
          <Outlet />
        </main>
        <footer className="app-footer">
          Trusted local deployment — no authentication by design. Do not expose this service to untrusted networks.
        </footer>
      </div>
    </Sheet>
  )
}
