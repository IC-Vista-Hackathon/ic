import type { Page, Route } from '@playwright/test';
import type { PaymentRequestObservation } from './contract';

// The deterministic server stub shared by the Playwright UX/a11y smoke and the F6 runtime
// contract gate. It mocks exactly the routes the payer flow talks to — config, invoice
// lookup, quote, payers, and payments — so the same fixed backend responses drive both the
// "flow reachable" smoke and the "requests conform" contract assertions.

export interface InvoiceFixture {
  id: string;
  account_number: string;
  payer_name: string;
  amount_cents: number;
  due_date: string;
  description: string;
  status: string;
  type?: string | null;
  status_color?: string | null;
  note?: string | null;
  note_emphasis?: boolean;
}

export const DEFAULT_ACCOUNT_NUMBER = '4421';

export const DEFAULT_INVOICE: InvoiceFixture = {
  id: 'inv-1',
  account_number: DEFAULT_ACCOUNT_NUMBER,
  payer_name: 'Test Payer',
  amount_cents: 12500,
  due_date: '2026-08-01',
  description: 'Monthly bill',
  status: 'due',
};

export const DEFAULT_QUOTE = { fee_cents: 250, total_cents: 12750 };

export const DEFAULT_CONFIRMATION = 'PRONTO-ABC123';

export const DEFAULT_PAYMENT_RESPONSE = {
  confirmation: DEFAULT_CONFIRMATION,
  amount_cents: 12500,
  fee_cents: 250,
  total_cents: 12750,
  status: 'succeeded',
};

// Records the payment POSTs the flow emits, at the network boundary, for the contract gate.
export interface PaymentRecorder {
  posts: PaymentRequestObservation[];
}

export function createPaymentRecorder(): PaymentRecorder {
  return { posts: [] };
}

export async function mockConfig(page: Page, definition: unknown): Promise<void> {
  await page.route(/\/api\/public\/experiences\//, route =>
    route.request().url().includes('manifest')
      ? route.fulfill({ contentType: 'application/manifest+json', body: '{}' })
      : route.fulfill({ contentType: 'application/json', body: JSON.stringify(definition) }),
  );
}

export async function mockInvoice(page: Page, invoices: InvoiceFixture[] = [DEFAULT_INVOICE]): Promise<void> {
  await page.route(/\/invoices\//, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ invoices }) }),
  );
}

export async function mockQuotes(page: Page, quote = DEFAULT_QUOTE): Promise<void> {
  await page.route(/\/payments\/quote/, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify(quote) }),
  );
}

// Guest checkout: payer lookup 404s, and any PATCH/POST to /payers is accepted so preference
// side-effects never fail the flow.
export async function mockGuestPayer(page: Page): Promise<void> {
  await page.route(/\/payers(\?|\/|$)/, route =>
    route.request().method() === 'GET'
      ? route.fulfill({ status: 404, contentType: 'application/json', body: '{}' })
      : route.fulfill({ contentType: 'application/json', body: '{}' }),
  );
}

export interface MockPaymentsOptions {
  response?: Record<string, unknown>;
  recorder?: PaymentRecorder;
  // Simulate latency so a double-submit races the first in-flight request.
  delayMs?: number;
}

export async function mockPayments(page: Page, options: MockPaymentsOptions = {}): Promise<void> {
  const response = options.response ?? DEFAULT_PAYMENT_RESPONSE;
  await page.route(/\/payments(\?|$)/, async (route: Route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      await route.fulfill({ contentType: 'application/json', body: '[]' });
      return;
    }
    if (options.recorder) {
      const rawBody = request.postData();
      options.recorder.posts.push({
        url: request.url(),
        method: request.method(),
        headers: request.headers(),
        body: safeJsonParse(rawBody),
        rawBody: rawBody ?? null,
      });
    }
    if (options.delayMs) await new Promise(resolve => setTimeout(resolve, options.delayMs));
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(response) });
  });
}

export interface HarnessOptions {
  definition: unknown;
  invoices?: InvoiceFixture[];
  quote?: typeof DEFAULT_QUOTE;
  paymentResponse?: Record<string, unknown>;
  recorder?: PaymentRecorder;
  delayMs?: number;
}

// Install the full deterministic happy-path stub in one call.
export async function installHarness(page: Page, options: HarnessOptions): Promise<void> {
  await mockConfig(page, options.definition);
  await mockInvoice(page, options.invoices);
  await mockQuotes(page, options.quote);
  await mockGuestPayer(page);
  await mockPayments(page, {
    response: options.paymentResponse,
    recorder: options.recorder,
    delayMs: options.delayMs,
  });
}

function safeJsonParse(raw: string | null | undefined): Record<string, unknown> | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}
