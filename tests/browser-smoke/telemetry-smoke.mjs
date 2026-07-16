// Browser telemetry smoke test for a deployed Pronto frontend (payer PWA or Biller Studio).
//
// Loads a deployed frontend through the target origin (usually a port-forwarded gateway), then
// asserts the frontend's telemetry behaviour:
//   1. GET /api/public/telemetry returns a non-null connection string (runtime config works),
//   2. the page boots and mints a flow id (the telemetry client initialized),
//   3. the Application Insights browser SDK POSTs the expected event carrying that flow id.
// It then inspects the ingestion response. A 200 is the happy path; a 439 ("Daily quota
// exceeded" — Application Insights throttling this resource) is an infrastructure/quota
// condition, NOT a frontend regression, so it is tolerated as a warning rather than failing the
// deploy. Because the throttle is intermittent, the check retries a few times to prefer a clean
// 200 before falling back to the warning. Any other non-200 status is a hard failure.
//
// Prints a single JSON object on stdout ({ flowId, expectedEvent, beaconSeen, ingestionStatus,
// throttled }); all logging goes to stderr so the caller can pipe stdout straight into jq.
//
// Works for both frontends: TARGET_PATH selects which app to load and EXPECTED_EVENT its
// session-start event; the sessionStorage flow-id key is derived from the event's app prefix
// (pwa.* -> pronto.pwa.flow_id, studio.* -> pronto.studio.flow_id). Defaults preserve the
// original PWA behavior.
//
// Usage: TARGET_ORIGIN=http://127.0.0.1:18081 node telemetry-smoke.mjs
//        TARGET_ORIGIN=http://127.0.0.1:18081 TARGET_PATH=/studio/ \
//          EXPECTED_EVENT=studio.session_started node telemetry-smoke.mjs

import { chromium } from 'playwright';

const origin = process.env.TARGET_ORIGIN ?? 'http://127.0.0.1:18081';
const pagePath = process.env.TARGET_PATH ?? '/pay/demo/';
const expectedEvent = process.env.EXPECTED_EVENT ?? 'pwa.session_started';
const flowStorageKey = `pronto.${expectedEvent.split('.')[0]}.flow_id`;
const beaconTimeoutMs = Number(process.env.BEACON_TIMEOUT_MS ?? 45_000);
// Application Insights ingestion throttling (HTTP 439) is intermittent on this resource, so retry
// a few attempts to prefer a clean 200 before tolerating the throttle.
const parsedAttempts = Number(process.env.SMOKE_ATTEMPTS ?? 3);
const maxAttempts = Number.isInteger(parsedAttempts) && parsedAttempts > 0 ? parsedAttempts : 3;
// Ingestion status returned when Application Insights is throttling the resource ("Daily quota
// exceeded"). This is an infra/quota condition, not a frontend regression.
const throttledStatus = 439;

const log = message => console.error(`[telemetry-smoke] ${message}`);
const fail = message => { log(`FAIL: ${message}`); process.exit(1); };

const configResponse = await fetch(`${origin}/api/public/telemetry`);
if (!configResponse.ok) fail(`/api/public/telemetry returned ${configResponse.status}`);
const config = await configResponse.json();
if (!config.connection_string) fail('/api/public/telemetry has no connection_string — telemetry env var missing on the API');
log('runtime telemetry configuration present');

// One attempt: load the page, confirm the telemetry client initialized (flow id) and the SDK
// POSTed the expected event with that flow id, then return the ingestion response for inspection.
// Frontend-behaviour failures (no flow id, no beacon, missing flow id in payload) hard-fail via
// fail() (process.exit(1)); only a throttled (439) return causes the caller to retry.
async function attempt(browser) {
  const page = await browser.newPage();
  page.on('console', message => log(`console.${message.type()}: ${message.text().slice(0, 200)}`));
  try {
    // Wait for the ingestion RESPONSE, not just the request — closing the page right after the
    // request starts aborts the in-flight POST and the event never reaches Application Insights.
    const beacon = page.waitForResponse(
      response => {
        const request = response.request();
        return request.method() === 'POST' &&
          (response.url().includes('applicationinsights.azure.com') || response.url().includes('/v2/track')) &&
          (request.postData() ?? '').includes(expectedEvent);
      },
      { timeout: beaconTimeoutMs },
    );

    log(`loading ${origin}${pagePath}`);
    await page.goto(`${origin}${pagePath}`, { waitUntil: 'domcontentloaded', timeout: 30_000 });

    await page.waitForFunction(key => sessionStorage.getItem(key) !== null, flowStorageKey, { timeout: 20_000 })
      .catch(() => fail('flow id never appeared in sessionStorage — telemetry client did not initialize'));
    const flowId = await page.evaluate(key => sessionStorage.getItem(key), flowStorageKey);
    log(`flow id ${flowId} (expecting event ${expectedEvent})`);

    const beaconResponse = await beacon.catch(() => fail(`no Application Insights beacon completed within ${beaconTimeoutMs}ms`));
    const beaconPayload = beaconResponse.request().postData() ?? '';
    if (!beaconPayload.includes(flowId)) fail(`the ${expectedEvent} beacon did not include flow_id ${flowId}`);
    const status = beaconResponse.status();
    const host = new URL(beaconResponse.url()).host;

    if (status === 200) {
      const receipt = await beaconResponse.json().catch(() => undefined);
      if (receipt && receipt.itemsAccepted < 1) fail(`ingestion accepted 0 items: ${JSON.stringify(receipt)}`);
      log(`${expectedEvent} beacon accepted by ${host} (itemsAccepted=${receipt?.itemsAccepted ?? 'unknown'})`);
      return { flowId, status, throttled: false };
    }
    if (status === throttledStatus) {
      // The SDK sent a well-formed beacon (correct event + flow id); ingestion is throttling.
      log(`ingestion at ${host} returned ${status} (throttled) — will retry`);
      return { flowId, status, throttled: true };
    }
    fail(`ingestion endpoint returned ${status}`);
  } finally {
    await page.close();
  }
}

const browser = await chromium.launch();
let accepted;
let lastThrottled;
try {
  for (let i = 1; i <= maxAttempts; i++) {
    log(`attempt ${i}/${maxAttempts}`);
    const result = await attempt(browser);
    if (!result.throttled) {
      accepted = result;
      break;
    }
    lastThrottled = result;
  }
} finally {
  await browser.close();
}

// Emit the result and let the process exit naturally (exit code 0): calling process.exit right
// after console.log can truncate stdout when it is a pipe (the deploy workflow captures this via
// command substitution and the docs pipe it into jq).
if (accepted) {
  console.log(JSON.stringify({ flowId: accepted.flowId, expectedEvent, beaconSeen: true, ingestionStatus: accepted.status, throttled: false }));
} else {
  // Every attempt confirmed the frontend emits the expected event, but Application Insights
  // ingestion kept throttling (439). Treat this as a non-fatal infra warning rather than failing
  // the deploy — the frontend behaviour under test is correct.
  // Write the workflow command directly (no `[telemetry-smoke] ` prefix): GitHub Actions only
  // recognizes `::warning::` as an annotation when it starts the line.
  console.error(`::warning::Application Insights ingestion throttled (${throttledStatus}) across ${maxAttempts} attempts; the ${expectedEvent} beacon was sent correctly but not accepted. This is an ingestion quota/throttle condition, not a frontend regression.`);
  console.log(JSON.stringify({ flowId: lastThrottled.flowId, expectedEvent, beaconSeen: true, ingestionStatus: lastThrottled.status, throttled: true }));
}
