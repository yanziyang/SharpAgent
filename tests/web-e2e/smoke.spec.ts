import { expect, test } from '@playwright/test'

test.describe('Phase 0 smoke', () => {
  // The dashboard fetches /api/health from the preview server; no backend is running
  // in the browser-only smoke, so the safe error card is the expected state.
  test('dashboard renders shell, health fallback, and theme persistence (E2E-17, E2E-20 seed)', async ({
    page,
  }) => {
    await page.goto('/')

    await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'New task' })).toBeVisible()

    // No-login MVP: the app starts on the product experience, never on a login route.
    await expect(page).not.toHaveURL(/login/i)

    // Health degrades safely without a backend behind the preview server.
    await expect(page.getByText('Health unavailable')).toBeVisible({ timeout: 15_000 })

    // Theme choice persists across reload.
    await page.getByLabel('Theme').selectOption('ocean')
    await page.reload()
    await expect(page.getByLabel('Theme')).toHaveValue('ocean')
  })
})
