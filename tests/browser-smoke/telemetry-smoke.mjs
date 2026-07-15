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

  // Wait for the ingestion RESPONSE, not just the request — closing the browser right after the
  // request starts aborts the in-flight POST and the event never reaches Application Insights.
  const beacon = page.waitForResponse(
    response => response.request().method() === 'POST' &&
      (response.url().includes('applicationinsights.azure.com') || response.url().includes('/v2/track')),
    { timeout: beaconTimeoutMs },
  );

  log(`loading ${origin}${pagePath}`);
  await page.goto(`${origin}${pagePath}`, { waitUntil: 'domcontentloaded', timeout: 30_000 });

  await page.waitForFunction(() => sessionStorage.getItem('pronto.pwa.flow_id') !== null, undefined, { timeout: 20_000 })
    .catch(() => fail('flow id never appeared in sessionStorage — telemetry client did not initialize'));
  const flowId = await page.evaluate(() => sessionStorage.getItem('pronto.pwa.flow_id'));
  log(`flow id ${flowId}`);

  const beaconResponse = await beacon.catch(() => fail(`no Application Insights beacon completed within ${beaconTimeoutMs}ms`));
  if (beaconResponse.status() !== 200) fail(`ingestion endpoint returned ${beaconResponse.status()}`);
  const receipt = await beaconResponse.json().catch(() => undefined);
  if (receipt && receipt.itemsAccepted < 1) fail(`ingestion accepted 0 items: ${JSON.stringify(receipt)}`);
  log(`beacon accepted by ${new URL(beaconResponse.url()).host} (itemsAccepted=${receipt?.itemsAccepted ?? 'unknown'})`);

  console.log(JSON.stringify({ flowId, beaconSeen: true }));
} finally {
  await browser.close();
}
