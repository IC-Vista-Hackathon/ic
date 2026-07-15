import { defineConfig, devices } from '@playwright/test';

// The bundle URL and mock config are provided by the pipeline (src/validate.ts) via env.
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: false,
  retries: 0,
  reporter: 'line',
  use: {
    baseURL: process.env.PAYER_URL,
    trace: 'off',
    // Block the PWA service worker so the gate's mocked routes intercept every fetch.
    serviceWorkers: 'block',
    ...devices['Desktop Chrome'],
  },
});
