import type { Page, Route } from '@playwright/test';

// Deterministic network stub for the Studio UI E2E. It fulfils exactly the Biller Experience API
// routes the onboarding → preview → publish journey talks to, plus the payer-PWA routes the
// embedded preview iframe hits, so the real Studio bundle (and the real payer PWA it embeds) run a
// full journey without any backend. Shapes mirror the shipped services (snake_case wire format).

export const BILLER_ID = 'biller-e2e';
export const PREVIEW_BILLER_ID = 'preview-e2e-1';

// Experience definition returned by create/chat/update/approve. Optional ui/preferences/billing are
// omitted — the Studio derives sensible defaults, matching a freshly drafted, not-yet-detailed config.
const DEFINITION = {
  schema_version: '1.0',
  biller_id: BILLER_ID,
  brand: { display_name: 'Springfield Water', primary_color: '#0B4F6C', secondary_color: '#01BAEF', logo_asset_id: null, font_family: null },
  content: { heading: 'Pay your water bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Springfield Water Payments', short_name: 'Springfield', theme_color: '#0B4F6C', background_color: '#ffffff', icon_asset_id: null },
  enabled_payment_capabilities: ['card', 'ach'],
};

const SESSION = {
  session_id: 'sess-1',
  biller_id: BILLER_ID,
  state: 'draft_ready',
  missing_fields: [],
  updated_at: '2026-01-01T00:00:00Z',
  discovery_progress: { completed: 3, total: 3, is_complete: true },
};

const revision = (state: string) => ({ biller_id: BILLER_ID, revision: 'rev-1', definition: DEFINITION, state, e_tag: 'etag-1', findings: [] });

const BOOTSTRAP = { biller: { biller_id: BILLER_ID, display_name: 'Springfield Water', slug: 'springfield-water' }, session: SESSION, draft: revision('draft') };
const CHAT = { reply: 'Your preview is ready to review.', session: SESSION, draft: revision('draft') };
const ACTIVITY = {
  session: SESSION,
  activity: [{ event_id: 'e1', sequence: 1, run_id: 'r1', agent_id: 'discovery', display_name: 'Discovery Agent', status: 'completed', summary: 'Analyzed the billing profile.', occurred_at: '2026-01-01T00:00:00Z' }],
};
const DEPLOYMENT = { deployment_id: 'dep-1', state: 'ready', revision: 'rev-1', published_url: 'https://pay.example.com/springfield-water' };
const PREVIEW_TENANT = { biller_id: BILLER_ID, preview_biller_id: PREVIEW_BILLER_ID, account_number: '4421', config_path: `billers/${PREVIEW_BILLER_ID}/preview.json` };

// What the embedded payer PWA fetches for the preview tenant. A valid base config (no preferences)
// so the shipped PWA boots and renders the payer landing — proving the iframe is not blank.
const PREVIEW_CONFIG = {
  schema_version: '1.0',
  biller_id: PREVIEW_BILLER_ID,
  brand: { display_name: 'Springfield Water', primary_color: '#0B4F6C', secondary_color: '#01BAEF', font_family: null },
  content: { heading: 'Pay your water bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Springfield Water Payments', short_name: 'Springfield', theme_color: '#0B4F6C', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};
const PREVIEW_INVOICE = { id: 'inv-1', account_number: '4421', payer_name: 'Pat Payer', amount_cents: 12100, due_date: '2026-08-01', description: 'Monthly water service', status: 'due' };
const PREVIEW_QUOTE = { fee_cents: 303, total_cents: 12403, amount_cents: 12100, outstanding_cents: 12100 };
const PREVIEW_PAYMENT = { confirmation: 'PRONTO-PREVIEW1', amount_cents: 12100, fee_cents: 303, total_cents: 12403, status: 'succeeded' };

export interface HarnessOptions {
  // Optional latency (ms) for the compliance gates (approve/publish), to exercise the slow
  // compliance path in the browser and prove the client budget outlives the generic 15s timeout.
  // Set this above 15s to make a regression to the generic budget abort at the browser boundary.
  gateDelayMs?: number;
}

function json(route: Route, body: unknown, status = 200): Promise<void> {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

// Install the full deterministic happy-path stub (Studio API + embedded payer PWA) in one call.
export async function installHarness(page: Page, options: HarnessOptions = {}): Promise<void> {
  const gateDelay = () => (options.gateDelayMs ? new Promise(resolve => setTimeout(resolve, options.gateDelayMs)) : Promise.resolve());

  // Live activity stream — fail fast; the journey completes from chat + activity snapshots.
  await page.route(/\/api\/billers\/[^/]+\/events$/, route => route.abort());
  await page.route(/\/api\/billers\/[^/]+\/preview\/reset$/, route => json(route, PREVIEW_TENANT));
  await page.route(/\/api\/billers\/[^/]+\/preview$/, route => json(route, PREVIEW_TENANT));
  await page.route(/\/api\/billers\/[^/]+\/chat$/, route => json(route, CHAT));
  await page.route(/\/api\/billers\/[^/]+\/activity$/, route => json(route, ACTIVITY));
  await page.route(/\/api\/billers\/[^/]+\/config\/approve$/, async route => { await gateDelay(); await json(route, revision('approved')); });
  await page.route(/\/api\/billers\/[^/]+\/config\/publish$/, async route => { await gateDelay(); await json(route, DEPLOYMENT); });
  await page.route(/\/api\/billers\/[^/]+\/config$/, route => json(route, revision('draft')));
  await page.route(/\/api\/billers\/[^/]+\/deployments\//, route => json(route, DEPLOYMENT));
  await page.route(/\/api\/billers$/, route => json(route, BOOTSTRAP));

  // Runtime telemetry config (both frontends fetch this at boot): no connection string → disabled.
  await page.route(/\/api\/public\/telemetry/, route => json(route, {}));

  // Embedded payer PWA (preview iframe) — same routes the shipped PWA talks to.
  await page.route(/\/api\/public\/experiences\//, route =>
    route.request().url().includes('manifest')
      ? route.fulfill({ contentType: 'application/manifest+json', body: '{}' })
      : json(route, PREVIEW_CONFIG));
  await page.route(/\/invoices\//, route => json(route, { invoices: [PREVIEW_INVOICE] }));
  await page.route(/\/payments\/quote/, route => json(route, PREVIEW_QUOTE));
  await page.route(/\/payers(\?|\/|$)/, route =>
    route.request().method() === 'GET' ? json(route, {}, 404) : json(route, {}));
  await page.route(/\/payments(\?|$)/, route =>
    route.request().method() === 'GET' ? json(route, []) : json(route, PREVIEW_PAYMENT));
}
