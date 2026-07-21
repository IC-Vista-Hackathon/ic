// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { settleInvoices } from './batch';
import { ASSISTANT_REQUEST_TIMEOUT_MS, ServicePaymentExperienceProvider } from './provider';
import { DEFAULT_REQUEST_TIMEOUT_MS } from './http';
import type { PaymentRequest } from './types';

interface PaymentPost {
  key: string | null;
  body: Record<string, unknown>;
}

function request(invoiceId: string, idempotencyKey: string): PaymentRequest {
  return { invoiceId, method: 'card', autoPay: false, paperless: false, accountNumber: '4421', idempotencyKey };
}

function paymentBody(invoiceId: string) {
  return JSON.stringify({ confirmation: `CONF-${invoiceId}`, amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' });
}

describe('ServicePaymentExperienceProvider.pay', () => {
  const posts: PaymentPost[] = [];

  beforeEach(() => { posts.length = 0; });
  afterEach(() => { vi.unstubAllGlobals(); });

  function stubFetch(onPayment?: (invoiceId: string) => Response) {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      if (url.includes('/payers') && method === 'GET') return new Response('{}', { status: 404 });
      if (url.includes('/payments') && method === 'POST') {
        const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
        const key = new Headers(init?.headers).get('idempotency-key');
        posts.push({ key, body });
        return onPayment ? onPayment(String(body.invoice_id)) : new Response(paymentBody(String(body.invoice_id)), { status: 200 });
      }
      throw new Error(`unexpected fetch ${method} ${url}`);
    }));
  }

  it('posts to /payments with the idempotency key and no client money fields', async () => {
    stubFetch();
    const provider = new ServicePaymentExperienceProvider('biller-1');
    await provider.pay(request('inv-1', 'key-1'));

    expect(posts).toHaveLength(1);
    expect(posts[0].key).toBe('key-1');
    expect(posts[0].body).toMatchObject({ biller_id: 'biller-1', invoice_id: 'inv-1', method: 'card' });
    // Amount, fee, and total are server-authoritative — never sent by the client.
    expect(posts[0].body).not.toHaveProperty('amount_cents');
    expect(posts[0].body).not.toHaveProperty('fee_cents');
    expect(posts[0].body).not.toHaveProperty('total_cents');
  });

  it('sends a requested partial amount as amount_cents', async () => {
    stubFetch();
    const provider = new ServicePaymentExperienceProvider('biller-1');
    await provider.pay({ ...request('inv-1', 'key-1'), amountCents: 2000 });

    expect(posts[0].body).toMatchObject({ invoice_id: 'inv-1', amount_cents: 2000 });
    expect(posts[0].body).not.toHaveProperty('installment_count');
  });

  it('sends an installment selection as installment_count and summarizes the returned plan', async () => {
    stubFetch(() => new Response(JSON.stringify({
      installment_plan_id: 'plan-1',
      installment_count: 3,
      total_amount_cents: 9000,
      installments: [
        { confirmation: 'CONF-A', amount_cents: 3000, fee_cents: 75, total_cents: 3075, status: 'scheduled', scheduled_for: '2026-08-01', installment_plan_id: 'plan-1' },
        { confirmation: 'CONF-B', amount_cents: 3000, fee_cents: 75, total_cents: 3075, status: 'scheduled', scheduled_for: '2026-09-01', installment_plan_id: 'plan-1' },
        { confirmation: 'CONF-C', amount_cents: 3000, fee_cents: 75, total_cents: 3075, status: 'scheduled', scheduled_for: '2026-10-01', installment_plan_id: 'plan-1' },
      ],
    }), { status: 201 }));
    const provider = new ServicePaymentExperienceProvider('biller-1');
    const completed = await provider.pay({ ...request('inv-1', 'key-1'), installmentCount: 3 });

    expect(posts[0].body).toMatchObject({ invoice_id: 'inv-1', installment_count: 3 });
    // The plan collapses to a receipt reporting the whole plan: principal, rolled-up fees/totals
    // (summed from the server-computed installments), first-installment confirmation, and the
    // 'scheduled' status since no installment has been charged yet.
    expect(completed.confirmation).toBe('CONF-A');
    expect(completed.amountCents).toBe(9000);
    expect(completed.feeCents).toBe(225); // 75 + 75 + 75
    expect(completed.totalCents).toBe(9225); // 3075 * 3, not just the first installment
    expect(completed.status).toBe('scheduled');
    expect(completed.installmentPlanId).toBe('plan-1');
    expect(completed.installmentCount).toBe(3);
  });

  it('N selected invoices produce N idempotent POSTs with distinct keys', async () => {
    stubFetch();
    const provider = new ServicePaymentExperienceProvider('biller-1');
    const requests = [request('inv-1', 'key-1'), request('inv-2', 'key-2'), request('inv-3', 'key-3')];

    const result = await settleInvoices(req => provider.pay(req), requests);

    expect(result.allSucceeded).toBe(true);
    expect(posts.map(post => post.body.invoice_id)).toEqual(['inv-1', 'inv-2', 'inv-3']);
    expect(new Set(posts.map(post => post.key)).size).toBe(3);
  });

  it('surfaces a partial failure without a second charge for the successful invoices', async () => {
    stubFetch(invoiceId => invoiceId === 'inv-2'
      ? new Response(JSON.stringify({ message: 'processor unavailable' }), { status: 503 })
      : new Response(paymentBody(invoiceId), { status: 200 }));
    const provider = new ServicePaymentExperienceProvider('biller-1');
    const requests = [request('inv-1', 'key-1'), request('inv-2', 'key-2'), request('inv-3', 'key-3')];

    const result = await settleInvoices(req => provider.pay(req), requests);

    expect(result.allSucceeded).toBe(false);
    expect(result.outcomes.find(outcome => outcome.invoiceId === 'inv-2')?.error).toBeDefined();
    // Each invoice is posted exactly once; a decline never re-charges a settled invoice.
    expect(posts.filter(post => post.body.invoice_id === 'inv-1')).toHaveLength(1);
    expect(posts.filter(post => post.body.invoice_id === 'inv-2')).toHaveLength(1);
    expect(posts.filter(post => post.body.invoice_id === 'inv-3')).toHaveLength(1);
  });
});

describe('ServicePaymentExperienceProvider.quote', () => {
  afterEach(() => { vi.unstubAllGlobals(); });

  it('passes the requested partial amount and maps the outstanding balance', async () => {
    const urls: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      urls.push(String(input));
      return new Response(JSON.stringify({ fee_cents: 50, total_cents: 2050, amount_cents: 2000, outstanding_cents: 5000 }), { status: 200 });
    }));
    const provider = new ServicePaymentExperienceProvider('biller-1');

    const result = await provider.quote('inv-1', 'card', 2000);

    expect(urls[0]).toContain('amount_cents=2000');
    expect(result).toEqual({ feeCents: 50, totalCents: 2050, amountCents: 2000, outstandingCents: 5000 });
  });

  it('omits amount_cents when quoting the full balance', async () => {
    const urls: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      urls.push(String(input));
      return new Response(JSON.stringify({ fee_cents: 0, total_cents: 5000, amount_cents: 5000, outstanding_cents: 5000 }), { status: 200 });
    }));
    const provider = new ServicePaymentExperienceProvider('biller-1');

    await provider.quote('inv-1', 'card');

    expect(urls[0]).not.toContain('amount_cents');
  });
});

describe('ServicePaymentExperienceProvider assistant turn budget', () => {
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  function assistantResponse() {
    return new Response(JSON.stringify({
      reply: 'Pay by card now to avoid the late fee.',
      artifacts: { payment_plan: { method: 'card', when: 'now', fee_cents: 250, total_cents: 12750, rationale: 'cheapest now' } },
    }), { status: 200 });
  }

  it.each([
    ['askAssistant', (provider: ServicePaymentExperienceProvider) => provider.askAssistant('inv-1', '4421')],
    ['chat', (provider: ServicePaymentExperienceProvider) => provider.chat('inv-1', '4421', [{ role: 'user', content: 'What should I do?' }])],
  ])('runs the payer-chat %s turn on the assistant budget, not the generic timeout', async (_name, call) => {
    const delays: number[] = [];
    // fetchWithTimeout arms the abort via window.setTimeout(_, timeoutMs); capture the budget it used.
    vi.spyOn(window, 'setTimeout').mockImplementation(((_handler: TimerHandler, delay?: number) => {
      delays.push(delay ?? 0); return 1 as unknown as ReturnType<typeof setTimeout>;
    }) as typeof setTimeout);
    vi.stubGlobal('fetch', vi.fn(async () => assistantResponse()));
    const provider = new ServicePaymentExperienceProvider('biller-1');

    await call(provider);

    // The payer-chat turn awaits a multi-stage LLM pipeline, so it must not inherit the 15s default
    // that produced the spurious "request timed out" seen on publish.
    expect(ASSISTANT_REQUEST_TIMEOUT_MS).toBeGreaterThan(DEFAULT_REQUEST_TIMEOUT_MS);
    expect(delays).toContain(ASSISTANT_REQUEST_TIMEOUT_MS);
    expect(delays).not.toContain(DEFAULT_REQUEST_TIMEOUT_MS);
  });
});
