import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, CHAT_REQUEST_TIMEOUT_MS } from './api';
import { DEFAULT_REQUEST_TIMEOUT_MS } from './http';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('Studio API request budgets', () => {
  it('allows the bounded research swarm to run beyond the generic request timeout', async () => {
    const delays: number[] = [];
    vi.stubGlobal('window', {
      setTimeout: (_callback: () => void, delay: number) => { delays.push(delay); return 1; },
      clearTimeout: () => undefined,
    });
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })));
    vi.spyOn(console, 'info').mockImplementation(() => undefined);

    await api.chat('biller-1', 'Research this biller');

    // Assert the relationship, not the literal: the chat budget must exceed the generic
    // request timeout so multi-agent research is never cut short by the default. The exact
    // value is a tuning knob and has changed twice already (120s -> 300s).
    expect(CHAT_REQUEST_TIMEOUT_MS).toBeGreaterThan(DEFAULT_REQUEST_TIMEOUT_MS);
    expect(delays).toEqual([CHAT_REQUEST_TIMEOUT_MS]);
  });
});

describe('Studio preview tenant lifecycle', () => {
  function stubFetch() {
    const calls: Array<{ url: string; method: string }> = [];
    vi.stubGlobal('window', {
      setTimeout: () => 1,
      clearTimeout: () => undefined,
    });
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({ url, method: init?.method ?? 'GET' });
      return new Response(JSON.stringify({
        biller_id: 'b1', preview_biller_id: 'preview-b1',
        account_number: '4421', config_path: '/public/experiences/preview/preview-b1',
      }), { status: 200, headers: { 'content-type': 'application/json' } });
    }));
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
    return calls;
  }

  it('provisions the preview tenant via POST /billers/{id}/preview', async () => {
    const calls = stubFetch();
    const tenant = await api.provisionPreview('b1');
    expect(tenant.preview_biller_id).toBe('preview-b1');
    expect(calls[0].method).toBe('POST');
    expect(calls[0].url).toContain('/billers/b1/preview');
    expect(calls[0].url).not.toContain('/preview/reset');
  });

  it('resets the preview tenant via POST /billers/{id}/preview/reset', async () => {
    const calls = stubFetch();
    await api.resetPreview('b1');
    expect(calls[0].method).toBe('POST');
    expect(calls[0].url).toContain('/billers/b1/preview/reset');
  });
});
