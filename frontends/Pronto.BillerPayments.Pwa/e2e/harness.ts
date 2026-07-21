import type { Page, Route } from '@playwright/test';

// Deterministic network stub for the payer PWA UI E2E. It fulfils exactly the routes the payer
// flow talks to — experience config, invoice lookup, quote, payers, payments, and the payer-chat
// assistant turn — so the real bundle runs a full journey without any backend. Mirrors the shape
// the shipped services return (snake_case wire format).

export interface InvoiceFixture {
  id: string;
  account_number: string;
  payer_name: string;
  amount_cents: number;
  due_date: string;
  description: string;
  status: string;
  type?: string | null;
}

export const DEFAULT_ACCOUNT_NUMBER = '4421';

export const DEFAULT_CONFIG = {
  schema_version: '1.0',
  biller_id: 'biller-demo',
  brand: { display_name: 'Springfield Water', primary_color: '#0B4F6C', secondary_color: '#01BAEF', font_family: null },
  content: { heading: 'Pay your water bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Springfield Water Payments', short_name: 'Springfield', theme_color: '#0B4F6C', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};

export const DEFAULT_INVOICE: InvoiceFixture = {
  id: 'inv-1',
  account_number: DEFAULT_ACCOUNT_NUMBER,
  payer_name: 'Pat Payer',
  amount_cents: 12500,
  due_date: '2026-08-01',
  description: 'Monthly water service',
  status: 'due',
};

export const DEFAULT_QUOTE = { fee_cents: 250, total_cents: 12750, amount_cents: 12500, outstanding_cents: 12500 };

export const DEFAULT_PAYMENT_RESPONSE = {
  confirmation: 'PRONTO-ABC123',
  amount_cents: 12500,
  fee_cents: 250,
  total_cents: 12750,
  status: 'succeeded',
};

export const DEFAULT_ASSISTANT_RESPONSE = {
  reply: 'Paying by card today avoids the late fee — that is the cheapest option right now.',
  artifacts: {
    payment_plan: { method: 'card', when: 'now', fee_cents: 250, total_cents: 12750, rationale: 'cheapest now' },
  },
};

export interface HarnessOptions {
  config?: Record<string, unknown>;
  invoices?: InvoiceFixture[];
  quote?: typeof DEFAULT_QUOTE;
  paymentResponse?: Record<string, unknown>;
  assistantResponse?: Record<string, unknown>;
  // Optional latency (ms) for the assistant turn, to exercise a slow agent path in the browser.
  assistantDelayMs?: number;
}

function json(route: Route, body: unknown, status = 200): Promise<void> {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
}

// Install the full deterministic happy-path stub in one call.
export async function installHarness(page: Page, options: HarnessOptions = {}): Promise<void> {
  const config = options.config ?? DEFAULT_CONFIG;
  const invoices = options.invoices ?? [DEFAULT_INVOICE];
  const quote = options.quote ?? DEFAULT_QUOTE;
  const paymentResponse = options.paymentResponse ?? DEFAULT_PAYMENT_RESPONSE;
  const assistantResponse = options.assistantResponse ?? DEFAULT_ASSISTANT_RESPONSE;

  // Runtime telemetry config fetched at boot: no connection string → telemetry stays disabled.
  await page.route(/\/api\/public\/telemetry/, route => json(route, {}));
  await page.route(/\/api\/public\/experiences\//, route =>
    route.request().url().includes('manifest')
      ? route.fulfill({ contentType: 'application/manifest+json', body: '{}' })
      : json(route, config));
  await page.route(/\/invoices\//, route => json(route, { invoices }));
  await page.route(/\/payments\/quote/, route => json(route, quote));
  await page.route(/\/payers(\?|\/|$)/, route =>
    route.request().method() === 'GET' ? json(route, {}, 404) : json(route, {}));
  await page.route(/\/api\/billers\/[^/]+\/payer-chat/, async route => {
    if (options.assistantDelayMs) await new Promise(resolve => setTimeout(resolve, options.assistantDelayMs));
    await json(route, assistantResponse);
  });
  await page.route(/\/payments(\?|$)/, route =>
    route.request().method() === 'GET' ? json(route, []) : json(route, paymentResponse));
}
