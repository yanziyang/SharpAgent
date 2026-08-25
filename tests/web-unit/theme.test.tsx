import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { ThemeProvider } from '@/shared/theme/theme-provider'
import { THEME_STORAGE_KEY } from '@/shared/theme/theme-context'
import { useTheme } from '@/shared/theme/use-theme'

function ThemeProbe() {
  const { theme, setTheme } = useTheme()
  return (
    <div>
      <span>current:{theme}</span>
      <button type="button" onClick={() => setTheme('ocean')}>
        pick-ocean
      </button>
    </div>
  )
}

describe('ThemeProvider', () => {
  beforeEach(() => {
    document.documentElement.dataset.theme = ''
  })

  it('rejects theme consumers outside the provider boundary', () => {
    expect(() => render(<ThemeProbe />)).toThrow(/useTheme must be used inside/i)
  })

  it('defaults to studio and applies the data-theme attribute', () => {
    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    )

    expect(screen.getByText('current:studio')).toBeInTheDocument()
    expect(document.documentElement.dataset.theme).toBe('studio')
  })

  it('applies and persists a selected theme', async () => {
    const user = userEvent.setup()

    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'pick-ocean' }))

    expect(document.documentElement.dataset.theme).toBe('ocean')
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('ocean')
  })

  it('restores a persisted theme on mount', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, 'forest')

    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    )

    expect(document.documentElement.dataset.theme).toBe('forest')
  })

  it('falls back to the default when storage holds an unknown value', () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, 'neon-rainbow')

    render(
      <ThemeProvider>
        <ThemeProbe />
      </ThemeProvider>,
    )

    expect(screen.getByText('current:studio')).toBeInTheDocument()
  })

  it('uses the default theme when storage access throws', () => {
    const throwingStorage = {
      length: 0,
      clear: () => undefined,
      getItem: () => {
        throw new Error('storage blocked')
      },
      key: () => null,
      removeItem: () => undefined,
      setItem: () => undefined,
    } satisfies Storage

    render(
      <ThemeProvider storage={throwingStorage}>
        <ThemeProbe />
      </ThemeProvider>,
    )

    expect(screen.getByText('current:studio')).toBeInTheDocument()
  })

  it('works without any persistence when storage is unavailable', async () => {
    const user = userEvent.setup()

    render(
      <ThemeProvider storage={null}>
        <ThemeProbe />
      </ThemeProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'pick-ocean' }))

    expect(document.documentElement.dataset.theme).toBe('ocean')
  })

  it('keeps the theme applied for the visit when persistence fails', async () => {
    const user = userEvent.setup()
    const originalStorage = globalThis.localStorage
    const failingStorage = {
      length: 0,
      clear: () => undefined,
      getItem: () => null,
      key: () => null,
      removeItem: () => undefined,
      setItem: () => {
        throw new Error('quota exceeded')
      },
    } satisfies Storage
    ;(globalThis as { localStorage?: Storage }).localStorage = failingStorage

    try {
      render(
        <ThemeProvider storage={failingStorage}>
          <ThemeProbe />
        </ThemeProvider>,
      )

      await user.click(screen.getByRole('button', { name: 'pick-ocean' }))

      expect(document.documentElement.dataset.theme).toBe('ocean')
    } finally {
      ;(globalThis as { localStorage?: Storage }).localStorage = originalStorage
    }
  })
})
