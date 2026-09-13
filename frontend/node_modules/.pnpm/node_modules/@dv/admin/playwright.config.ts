import { defineConfig, devices } from '@playwright/test';

/**
 * Drives the admin panel against a running API. The dev server proxies `/api` to the backend.
 *
 * `E2E_BASE_URL` moves the whole run to another port. 5174 is what Vite picks when 5173 is taken,
 * so another project open on this machine can be listening on it — and `reuseExistingServer` then
 * attaches to that one and runs the suite against somebody else's panel, failing at the login form
 * in ways that look like our bugs. Overriding the port is the way out, since killing the other
 * server is not ours to do.
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:5174';
const port = new URL(baseURL).port || '5174';

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
