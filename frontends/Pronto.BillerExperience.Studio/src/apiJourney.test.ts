// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from './api';
import { DEFAULT_REQUEST_TIMEOUT_MS, fetchWithTimeout } from './http';

// End-to-end journey guard for the Studio client stack. Instead of hand-checking each endpoint's
// timeout, this walks the whole biller journey (create → chat → save → approve → publish →
// provision preview) through the REAL api.ts + fetchWithTimeout, with a fake backend that answers
// the agentic gates (chat/approve/publish) only after 60s — four times the generic 15s budget.
// If any of those gated calls is (re)wired to the short default timeout, its AbortController fires
// at 15s, the call rejects with `request_timeout`, and this journey fails — the same class of bug
// that produced the "Could not publish. The request timed out." report, caught automatically.
const SLOW_AGENT_MS = 60_000;

// The client applies no runtime schema to these responses (it returns response.json() as T), so a
// minimal body per endpoint is enough to exercise the real request/timeout path end to end.
const bodies: Record<string, unknown> = {
  create: { biller: { biller_id: 'b1' }, session: {}, draft: { id: 'config-1' } },
  chat: { session: {}, draft: { id: 'config-1' } },
  update: { id: 'config-1' },
  approve: { id: 'config-1', state: 'approved' },
  publish: { deployment_id: 'deployment-1', status: 'requested' },
  preview: { biller_id: 'b1', preview_biller_id: 'preview-b1', account_number: '4421' },
};

// Endpoints that synchronously run agents/LLM work server-side, so a real slow backend answers
// them only after SLOW_AGENT_MS. Everything else answers immediately.
function delayFor(url: string, method: string): number {
  if (url.endsWith('/chat') && method === 'POST') return SLOW_AGENT_MS;
  if (url.endsWith('/config/approve') && method === 'POST') return SLOW_AGENT_MS;
  if (url.endsWith('/config/publish') && method === 'POST') return SLOW_AGENT_MS;
  return 0;
}

function bodyFor(url: string, method: string): unknown {
  if (url.endsWith('/config/approve')) return bodies.approve;
  if (url.endsWith('/config/publish')) return bodies.publish;
  if (url.endsWith('/chat')) return bodies.chat;
  if (url.endsWith('/preview')) return bodies.preview;
  if (url.endsWith('/config') && method === 'PATCH') return bodies.update;
  if (url.endsWith('/billers') && method === 'POST') return bodies.create;
  return {};
}

describe('Studio onboarding → publish journey survives slow agent gates', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      const body = JSON.stringify(bodyFor(url, method));
      const response = new Response(body, { status: 200, headers: { 'content-type': 'application/json' } });
      const delay = delayFor(url, method);
      if (delay === 0) return Promise.resolve(response);
      // Honor the AbortController the way real fetch does: if the caller's timeout fires before the
      // backend responds, the fetch rejects — this is what makes a too-short budget observable.
      return new Promise<Response>((resolve, reject) => {
        const signal = init?.signal;
        const timer = setTimeout(() => resolve(response), delay);
        signal?.addEventListener('abort', () => { clearTimeout(timer); reject(new DOMException('Aborted', 'AbortError')); });
      });
    }));
  });
  afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  // Advance the fake clock past the slow-backend delay and settle the call. If the request armed a
  // 15s abort, advancing 60s trips it before the backend responds and the promise rejects here.
  async function settle<T>(pending: Promise<T>): Promise<T> {
    await vi.advanceTimersByTimeAsync(SLOW_AGENT_MS + 1_000);
    return pending;
  }

  it('completes every step end to end without a spurious request timeout', async () => {
    const bootstrap = await settle(api.create({ display_name: 'Brownsville PUB', slug: 'brownsville', bill_type: 'utility', postal_code: '78520' }));
    expect(bootstrap.biller.biller_id).toBe('b1');

    // The three agentic gates each answer only after 60s; each must ride the longer budget.
    await expect(settle(api.chat('b1', 'Make it feel trustworthy'))).resolves.toBeDefined();
    await expect(settle(api.update('b1', {} as never))).resolves.toBeDefined();
    await expect(settle(api.approve('b1', 'config-1'))).resolves.toBeDefined();

    const deployment = await settle(api.publish('b1', 'config-1'));
    expect(deployment).toMatchObject({ status: 'requested' });

    await expect(settle(api.provisionPreview('b1'))).resolves.toMatchObject({ preview_biller_id: 'preview-b1' });
  });

  // Control: proves the harness is sound — a request armed at the generic 15s budget genuinely
  // aborts against the same 60s backend. This is exactly what the journey above would suffer if a
  // gated call regressed to the default, so the passing journey is a real guarantee, not a no-op.
  it('a default-budget request against the slow backend does time out (harness soundness)', async () => {
    const pending = fetchWithTimeout('/billers/b1/config/publish', { method: 'POST' }, DEFAULT_REQUEST_TIMEOUT_MS);
    // Attach the rejection expectation before advancing the clock, so the handler is in place when
    // the 15s abort fires mid-advance (otherwise the rejection is briefly unhandled).
    const assertion = expect(pending).rejects.toMatchObject({ code: 'request_timeout' });
    await vi.advanceTimersByTimeAsync(SLOW_AGENT_MS + 1_000);
    await assertion;
  });
});
