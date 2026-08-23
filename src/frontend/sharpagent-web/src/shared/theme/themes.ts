export const THEMES = ['studio', 'midnight', 'ocean', 'forest'] as const

export type ThemeId = (typeof THEMES)[number]

export const DEFAULT_THEME: ThemeId = 'studio'

export const THEME_LABELS: Record<ThemeId, string> = {
  studio: 'Studio',
  midnight: 'Midnight',
  ocean: 'Ocean',
  forest: 'Forest',
}

export function isThemeId(value: unknown): value is ThemeId {
  return typeof value === 'string' && (THEMES as readonly string[]).includes(value)
}
