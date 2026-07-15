// Browser telemetry smoke test for the deployed payer PWA.
//
// Loads the PWA through the target origin (usually a port-forwarded gateway), then asserts:
//   1. GET /api/public/telemetry returns a non-null connection string (runtime config works),
//   2. the page boots and mints a flow id (the telemetry client initialized),
//   3. the Application Insights browser SDK actually POSTs a beacon off the page.
//
// Prints a single JSON object on stdout ({ flowId, beaconSeen }); all logging goes to stderr so
// the caller can pipe stdout straight into jq. The caller then verifies server-side arrival by
// querying Application Insights for a pwa.session_started event carrying this flow id.
//
// Usage: TARGET_ORIGIN=http://127.0.0.1:18081 node telemetry-smoke.mjs

import { chromium } from 'playwright';

const origin = process.env.TARGET_ORIGIN ?? 'http://127.0.0.1:18081';
const pagePath = process.env.TARGET_PATH ?? '/pay/demo/';
const beaconTimeoutMs = Number(process.env.BEACON_TIMEOUT_MS ?? 45_000);

const log = message => console.error(`[telemetry-smoke] ${message}`);
const fail = message => { log(`FAIL: ${message}`); process.exit(1); };

const configResponse = await fetch(`${origin}/api/public/telemetry`);
if (!configResponse.ok) fail(`/api/public/telemetry returned ${configResponse.status}`);
const config = await configResponse.json();
if (!config.connection_string) fail('/api/public/telemetry has no connection_string — telemetry env var missing on the API');
log('runtime telemetry configuration present');

const browser = await chromium.launch();
try {
  const page = await browser.newPage();
  page.on('console', message => log(`console.${message.type()}: ${message.text().slice(0, 200)}`));

  const beacon = page.waitForRequest(
    request => request.method() === 'POST' &&
      (request.url().includes('applicationinsights.azure.com') || request.url().includes('/v2/track')),
    { timeout: beaconTimeoutMs },
  );

  log(`loading ${origin}${pagePath}`);
  await page.goto(`${origin}${pagePath}`, { waitUntil: 'domcontentloaded', timeout: 30_000 });

  await page.waitForFunction(() => sessionStorage.getItem('pronto.pwa.flow_id') !== null, undefined, { timeout: 20_000 })
    .catch(() => fail('flow id never appeared in sessionStorage — telemetry client did not initialize'));
  const flowId = await page.evaluate(() => sessionStorage.getItem('pronto.pwa.flow_id'));
  log(`flow id ${flowId}`);

  const beaconRequest = await beacon.catch(() => fail(`no Application Insights beacon left the page within ${beaconTimeoutMs}ms`));
  log(`beacon sent to ${new URL(beaconRequest.url()).host}`);

  console.log(JSON.stringify({ flowId, beaconSeen: true }));
} finally {
  await browser.close();
}
