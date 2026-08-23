import { defineConfig, devices } from '@playwright/test'

// Browser end-to-end tests live here; the app is served from a production build.
const appDir = '../../src/frontend/sharpagent-web'

export default defineConfig({
  testDir: '.',
  outputDir: 'test-results',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['html', { open: 'never' }]],
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'npm run build && npm run preview -- --port 4173 --strictPort',
    cwd: appDir,
    url: 'http://localhost:4173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [
    // Phase 0 smoke runs in Chromium only; the full three-engine critical suite
    // arrives with the complete Playwright coverage phase (Implementation Plan 15.2).
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
})
