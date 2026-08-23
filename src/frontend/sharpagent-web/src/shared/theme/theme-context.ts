import { createContext } from 'react'
import type { ThemeId } from './themes'

export const THEME_STORAGE_KEY = 'sharpagent.theme'

export interface ThemeContextValue {
  theme: ThemeId
  setTheme: (theme: ThemeId) => void
}

export const ThemeContext = createContext<ThemeContextValue | null>(null)
