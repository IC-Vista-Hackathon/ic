import os from 'node:os';
import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

// The bundle URL and mock config are provided by the pipeline (src/validate.ts) via env.
//
// Playwright always creates its output directory on start. In the build Job the image tree
// (/app) is not a writable scratch location — only the emptyDir mount (HOME=/work, PAYER_WORK)
// is — so the default ./test-results under /app fails with EACCES. Anchor the output dir inside
// the writable work mount so no writes land outside it.
const outputDir = path.resolve(
  process.env.PAYER_WORK ?? process.env.HOME ?? os.tmpdir(),
  'playwright-results',
);

export default defineConfig({
  testDir: './e2e',
  outputDir,
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
