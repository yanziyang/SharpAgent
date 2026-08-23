import { NavLink, Outlet } from 'react-router'
import { ThemeMenu } from '@/shared/theme/theme-menu'

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/sessions/new', label: 'New task' },
  { to: '/sessions/archive', label: 'Archive' },
  { to: '/settings/workspaces', label: 'Workspaces' },
  { to: '/settings/models', label: 'Models' },
  { to: '/settings/policy', label: 'Policy' },
  { to: '/settings/runtime', label: 'Runtime' },
  { to: '/settings/appearance', label: 'Appearance' },
]

export function RootLayout() {
  return (
    <div className="flex min-h-svh flex-col">
      <header className="flex items-center justify-between gap-4 border-b px-4 py-3 md:px-6">
        <div className="flex items-center gap-3">
          <span aria-hidden className="size-6 rounded-md bg-primary" />
          <span className="text-sm font-semibold tracking-tight">SharpAgent</span>
        </div>
        <nav aria-label="Primary">
          <ul className="hidden items-center gap-1 text-sm md:flex">
            {NAV_ITEMS.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  end={'end' in item ? item.end : false}
                  className={({ isActive }) =>
                    `rounded-md px-2 py-1 ${isActive ? 'bg-accent text-accent-foreground font-medium' : 'text-muted-foreground hover:text-foreground'}`
                  }
                >
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <ThemeMenu />
      </header>
      <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 md:px-6">
        <Outlet />
      </main>
      <footer className="border-t px-4 py-3 md:px-6">
        <p className="text-xs text-muted-foreground">
          Trusted local deployment — no authentication by design. Do not expose this service to untrusted networks.
        </p>
      </footer>
    </div>
  )
}
