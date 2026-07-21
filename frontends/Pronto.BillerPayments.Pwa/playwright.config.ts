import { defineConfig, devices } from '@playwright/test';

// Deterministic UI end-to-end tests for the shipped Payer PWA. Unlike the vitest suites (which
// mock the provider) these drive the REAL built bundle in a real browser with the backend faked at
// the network boundary (page.route), so they catch white-screen boots, broken renders, and dead
// buttons that unit tests never see. The build enables the payer assistant so its turn is covered.
// vite serves the built bundle under the `/pay/` base path and binds to localhost.
const PORT = 4174;
const BASE_PATH = '/pay/';

export default defineConfig({
  testDir: './e2e',
  // Generous per-test budget: the slow-assistant test intentionally delays the payer-chat reply
  // past the generic 15s request budget to prove the turn resolves on the longer assistant budget.
  timeout: 60_000,
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'line',
  use: {
    baseURL: `http://localhost:${PORT}`,
    trace: 'on-first-retry',
    // Block the PWA service worker so the mocked routes intercept every fetch.
    serviceWorkers: 'block',
    ...devices['Desktop Chrome'],
  },
  webServer: {
    // Build + serve the real bundle. VITE_PAYER_ASSISTANT is a build-time flag, so it must be set
    // for the build step, not just preview, for the assistant to render.
    command: `npm run build && npm run preview -- --strictPort`,
    url: `http://localhost:${PORT}${BASE_PATH}`,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: { VITE_PAYER_ASSISTANT: 'true' },
  },
});
