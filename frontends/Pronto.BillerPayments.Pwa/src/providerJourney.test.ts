// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ServicePaymentExperienceProvider } from './provider';
import { DEFAULT_REQUEST_TIMEOUT_MS, fetchWithTimeout } from './http';

// End-to-end journey guard for the Payer PWA client stack. It walks the whole payer flow
// (find bill → quote → ask assistant → pay) through the REAL provider + fetchWithTimeout, with a
// fake backend that answers the agentic payer-chat turn only after 60s — four times the generic
// 15s budget. If payer-chat is (re)wired to the short default timeout, its AbortController fires at
// 15s, the assistant turn rejects, and this journey fails — the same class of bug as the publish
// timeout, caught automatically instead of by manual clicking.
const SLOW_AGENT_MS = 60_000;

function bodyFor(url: string, method: string): unknown {
  if (url.includes('/payer-chat')) {
    return { reply: 'Pay by card now to avoid the late fee.', artifacts: { payment_plan: { method: 'card', when: 'now', fee_cents: 250, total_cents: 12750, rationale: 'cheapest now' } } };
  }
  if (url.includes('/invoices')) {
    return { invoices: [{ id: 'inv-1', account_number: '4421', payer_name: 'Pat', amount_cents: 12500, due_date: '2026-08-01', description: 'Water bill', status: 'due' }] };
  }
  if (url.includes('/payments/quote')) return { fee_cents: 250, total_cents: 12750, amount_cents: 12500, outstanding_cents: 12500 };
  if (url.includes('/payers') && method === 'GET') return {};
  if (url.includes('/payments') && method === 'POST') return { confirmation: 'CONF-1', amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' };
  return {};
}

// Only the payer-chat turn runs agents server-side, so only it answers slowly.
function delayFor(url: string): number {
  return url.includes('/payer-chat') ? SLOW_AGENT_MS : 0;
}

describe('Payer PWA pay journey survives a slow assistant turn', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      const status = url.includes('/payers') && method === 'GET' ? 404 : 200;
      const response = new Response(JSON.stringify(bodyFor(url, method)), { status, headers: { 'content-type': 'application/json' } });
      const delay = delayFor(url);
      if (delay === 0) return Promise.resolve(response);
      // Honor the AbortController like real fetch: a too-short client budget must reject the fetch.
      return new Promise<Response>((resolve, reject) => {
        const timer = setTimeout(() => resolve(response), delay);
        init?.signal?.addEventListener('abort', () => { clearTimeout(timer); reject(new DOMException('Aborted', 'AbortError')); });
      });
    }));
  });
  afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  async function settle<T>(pending: Promise<T>): Promise<T> {
    await vi.advanceTimersByTimeAsync(SLOW_AGENT_MS + 1_000);
    return pending;
  }

  it('finds the bill, quotes, gets an assistant recommendation, and pays — all without a timeout', async () => {
    const provider = new ServicePaymentExperienceProvider('biller-1');

    const invoices = await provider.getInvoices('4421');
    expect(invoices[0].id).toBe('inv-1');

    const quote = await provider.quote('inv-1', 'card');
    expect(quote.totalCents).toBe(12750);

    // The assistant turn answers only after 60s; it must ride the longer budget, not the 15s default.
    const recommendation = await settle(provider.askAssistant('inv-1', '4421'));
    expect(recommendation.reply).toContain('card');
    expect(recommendation.totalCents).toBe(12750);

    const receipt = await provider.pay({ invoiceId: 'inv-1', method: 'card', autoPay: false, paperless: false, accountNumber: '4421', idempotencyKey: 'key-1' });
    expect(receipt.confirmation).toBe('CONF-1');
    expect(receipt.status).toBe('succeeded');
  });

  // Control: a request armed at the generic 15s budget genuinely aborts against the same 60s
  // backend — proving the assistant leg above is a real guarantee, not a mock that ignores aborts.
  it('a default-budget request against the slow backend does time out (harness soundness)', async () => {
    const pending = fetchWithTimeout('/api/billers/biller-1/payer-chat', { method: 'POST' }, DEFAULT_REQUEST_TIMEOUT_MS);
    const assertion = expect(pending).rejects.toMatchObject({ code: 'request_timeout' });
    await vi.advanceTimersByTimeAsync(SLOW_AGENT_MS + 1_000);
    await assertion;
  });
});
