import { defineConfig, devices } from '@playwright/test';

// Deterministic UI end-to-end test for the shipped Biller Experience Studio. Unlike the vitest
// suites (which mock the API module) this drives the REAL built Studio bundle in a real browser,
// AND embeds the REAL built payer PWA in the preview iframe — the exact regression the user hit was
// a blank preview iframe, so the test only passes if the shipped PWA actually renders inside it.
// The backend is faked at the network boundary (page.route) for both frontends.
//
// Two servers: the Studio (served under /studio/) and the payer PWA (served under /pay/). The Studio
// build is pointed at the PWA server via VITE_PWA_PREVIEW_URL so PreviewPane embeds the real bundle.
const STUDIO_PORT = 4173;
const PWA_PORT = 4174;
const PWA_PREVIEW_URL = `http://localhost:${PWA_PORT}/pay/`;

export default defineConfig({
  testDir: './e2e',
  // Generous per-test budget: the slow-publish test intentionally delays the compliance gates past
  // the generic 15s request budget to prove the client waits it out.
  timeout: 90_000,
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: 'line',
  use: {
    baseURL: `http://localhost:${STUDIO_PORT}`,
    trace: 'on-first-retry',
    // Block the PWA service worker so the mocked routes intercept every fetch in the iframe.
    serviceWorkers: 'block',
    ...devices['Desktop Chrome'],
  },
  webServer: [
    {
      // Build + serve the real Studio, pointed at the local PWA server for its preview iframe.
      command: `npm run build && npm run preview -- --strictPort`,
      url: `http://localhost:${STUDIO_PORT}/studio/`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: { VITE_PWA_PREVIEW_URL: PWA_PREVIEW_URL },
    },
    {
      // Build + serve the real payer PWA the Studio embeds in its preview iframe.
      command: `npm --prefix ../Pronto.BillerPayments.Pwa run build && npm --prefix ../Pronto.BillerPayments.Pwa run preview -- --strictPort`,
      url: PWA_PREVIEW_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
});
