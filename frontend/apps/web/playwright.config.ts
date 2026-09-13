import { defineConfig, devices } from '@playwright/test';

/**
 * Drives the applicant app against a running API. The dev server proxies `/api`, so the tests
 * exercise the same request path a real browser takes.
 *
 * `E2E_BASE_URL` moves the whole run to another port. 5173 is Vite's default, so any other project
 * open on this machine can be listening on it — and `reuseExistingServer` then attaches to that
 * one and runs the entire suite against somebody else's app, failing in ways that look like our
 * bugs. Overriding the port is the way out, since killing the other server is not ours to do.
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5173';
const port = new URL(baseURL).port || '5173';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `pnpm dev --port ${port} --strictPort`,
    url: baseURL,
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
