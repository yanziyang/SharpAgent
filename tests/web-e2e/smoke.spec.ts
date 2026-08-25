import { expect, test } from '@playwright/test'

test.describe('Phase 6 browser smoke', () => {
  // The dashboard fetches /api/health from the preview server; no backend is running
  // in the browser-only smoke, so the safe error card is the expected state.
  test('dashboard renders shell, health fallback, and theme persistence (E2E-17, E2E-20 seed)', async ({
    page,
  }) => {
    await page.goto('/')

    await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'New session' })).toBeVisible()

    // No-login MVP: the app starts on the product experience, never on a login route.
    await expect(page).not.toHaveURL(/login/i)

    // Health degrades safely without a backend behind the preview server.
    await expect(page.getByText('Health unavailable')).toBeVisible({ timeout: 15_000 })

    // Theme choice persists across reload.
    await page.getByLabel('Theme').selectOption('ocean')
    await page.reload()
    await expect(page.getByLabel('Theme')).toHaveValue('ocean')
  })

  test('tablet navigation uses an accessible Sheet and restores focus (E2E-18)', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 })
    await page.goto('/')

    await page.getByRole('button', { name: 'Toggle navigation' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    // Base UI moves focus to the first navigation control inside the modal.
    await expect(page.getByRole('tab', { name: 'Home' })).toBeFocused()

    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).not.toBeVisible()
    await expect(page.getByRole('button', { name: 'Toggle navigation' })).toBeFocused()
  })
})
