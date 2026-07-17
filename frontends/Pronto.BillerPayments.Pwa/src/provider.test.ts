// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { settleInvoices } from './batch';
import { ServicePaymentExperienceProvider } from './provider';
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
