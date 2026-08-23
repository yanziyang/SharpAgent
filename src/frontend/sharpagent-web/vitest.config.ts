import { fileURLToPath, URL } from 'node:url'
import path from 'node:path'
import { defineConfig } from 'vitest/config'

// Unit tests live in tests/web-unit at the repository root; the npm workspace at
// the repository root provides one shared dependency tree for app and tests.
const unitTestsRoot = fileURLToPath(new URL('../../../tests/web-unit/', import.meta.url)).replace(/\\/g, '/')
const appRoot = fileURLToPath(new URL('./', import.meta.url)).replace(/\\/g, '/')
const appSrc = `${appRoot}src`

export default defineConfig({
  server: {
    // Allow transforming test files that live outside the app directory.
    fs: {
      allow: [appRoot, unitTestsRoot],
    },
  },
  resolve: {
    alias: {
      '@': appSrc,
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: [path.join(unitTestsRoot, 'setup.ts')],
    include: [`${unitTestsRoot}/**/*.test.{ts,tsx}`],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'lcov'],
      reportsDirectory: `${appRoot}coverage/web-unit`,
      all: true,
      include: [`${appSrc}/**`],
      exclude: [
        `${appSrc}/**/*.d.ts`,
        `${appSrc}/main.tsx`,
        // Non-executable assets.
        `${appSrc}/assets/**`,
        `${appSrc}/**/*.css`,
        // Generated shadcn/ui primitives are exercised indirectly; product code must be covered.
        `${appSrc}/components/ui/**`,
        `${appSrc}/lib/utils.ts`,
      ],
      thresholds: {
        lines: 91,
        branches: 91,
        functions: 91,
        statements: 91,
      },
    },
  },
})
