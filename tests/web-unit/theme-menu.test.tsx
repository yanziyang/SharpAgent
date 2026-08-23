import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ThemeMenu } from '@/shared/theme/theme-menu'
import { ThemeProvider } from '@/shared/theme/theme-provider'

describe('ThemeMenu', () => {
  it('offers exactly the four approved themes', async () => {
    const user = userEvent.setup()

    render(
      <ThemeProvider>
        <ThemeMenu />
      </ThemeProvider>,
    )

    const select = screen.getByRole('combobox', { name: 'Theme' })
    const options = [...select.options].map((option) => option.value)
    expect(options).toEqual(['studio', 'midnight', 'ocean', 'forest'])

    await user.selectOptions(select, 'forest')

    expect(document.documentElement.dataset.theme).toBe('forest')
    expect(window.localStorage.getItem('sharpagent.theme')).toBe('forest')
  })
})
