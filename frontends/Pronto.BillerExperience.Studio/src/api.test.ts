import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, CHAT_REQUEST_TIMEOUT_MS } from './api';

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

    expect(CHAT_REQUEST_TIMEOUT_MS).toBe(300_000);
    expect(delays).toEqual([CHAT_REQUEST_TIMEOUT_MS]);
  });
});
