import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { THEME_STORAGE_KEY, ThemeContext } from './theme-context'
import { DEFAULT_THEME, isThemeId, type ThemeId } from './themes'

function readStoredTheme(storage: Storage | null): ThemeId {
  if (!storage) {
    return DEFAULT_THEME
  }

  try {
    const raw = storage.getItem(THEME_STORAGE_KEY)
    return isThemeId(raw) ? raw : DEFAULT_THEME
  } catch {
    return DEFAULT_THEME
  }
}

export interface ThemeProviderProps {
  children: ReactNode
  storage?: Storage | null
}

/**
 * Persists only the visual theme preference in browser storage; all product
 * state stays server-authoritative per the functional specification.
 */
export function ThemeProvider({ children, storage = globalThis.localStorage }: ThemeProviderProps) {
  const [theme, setThemeState] = useState<ThemeId>(() => readStoredTheme(storage))

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    return () => {
      delete document.documentElement.dataset.theme
    }
  }, [theme])

  const setTheme = useCallback(
    (next: ThemeId) => {
      setThemeState(next)
      try {
        storage?.setItem(THEME_STORAGE_KEY, next)
      } catch {
        // Storage may be unavailable (private mode); theme still applies for this visit.
      }
    },
    [storage],
  )

  const value = useMemo(() => ({ theme, setTheme }), [theme, setTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
