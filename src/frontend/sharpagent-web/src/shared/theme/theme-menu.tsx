import { useTheme } from '@/shared/theme/use-theme'
import { THEMES, THEME_LABELS, type ThemeId } from '@/shared/theme/themes'

/** Selects one of the four approved themes; persists the choice locally. */
export function ThemeMenu() {
  const { theme, setTheme } = useTheme()

  const handleChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
    setTheme(event.target.value as ThemeId)
  }

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-muted-foreground">Theme</span>
      <select
        aria-label="Theme"
        className="h-8 rounded-md border border-input bg-background px-2 text-foreground focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none"
        value={theme}
        onChange={handleChange}
      >
        {THEMES.map((id) => (
          <option key={id} value={id}>
            {THEME_LABELS[id]}
          </option>
        ))}
      </select>
    </label>
  )
}
