import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

interface FakeInstance {
  config: Record<string, unknown>;
  loaded: boolean;
  initializers: Array<(item: unknown) => boolean | void>;
  events: Array<{ name: string; properties: Record<string, string> }>;
}

const instances: FakeInstance[] = [];

vi.mock('@microsoft/applicationinsights-web', () => ({
  ApplicationInsights: class {
    config: Record<string, unknown>;
    loaded = false;
    initializers: Array<(item: unknown) => boolean | void> = [];
    events: Array<{ name: string; properties: Record<string, string> }> = [];
    constructor(options: { config: Record<string, unknown> }) {
      this.config = options.config;
      instances.push(this as unknown as FakeInstance);
    }
    loadAppInsights() { this.loaded = true; }
    addTelemetryInitializer(initializer: (item: unknown) => boolean | void) { this.initializers.push(initializer); }
    trackEvent(event: { name: string }, properties: Record<string, string>) { this.events.push({ name: event.name, properties }); }
  },
}));

import { enforceAllowlist, flowId, initBrowserTelemetry, resetBrowserTelemetryForTests, trackEvent } from './insights';

function stubConfigResponse(body: unknown, ok = true) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok, json: () => Promise.resolve(body) }));
}

beforeEach(() => {
  instances.length = 0;
  resetBrowserTelemetryForTests();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('initBrowserTelemetry', () => {
  it('stays disabled when no connection string is configured', async () => {
    stubConfigResponse({ connection_string: null, sampling_percentage: 100 });
    expect(await initBrowserTelemetry()).toBe(false);
    expect(instances).toHaveLength(0);
    expect(() => trackEvent('pwa.session_started')).not.toThrow();
  });

  it('stays disabled when the config endpoint fails', async () => {
    stubConfigResponse({}, false);
    expect(await initBrowserTelemetry()).toBe(false);
    expect(instances).toHaveLength(0);
  });

  it('stays disabled when fetch itself throws', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
    expect(await initBrowserTelemetry()).toBe(false);
  });

  it('starts the client with all automatic collection off', async () => {
    stubConfigResponse({ connection_string: 'InstrumentationKey=x', sampling_percentage: 50 });
    expect(await initBrowserTelemetry()).toBe(true);

    const [instance] = instances;
    expect(instance.loaded).toBe(true);
    expect(instance.initializers).toHaveLength(1);
    expect(instance.config).toMatchObject({
      connectionString: 'InstrumentationKey=x',
      samplingPercentage: 50,
      disableAjaxTracking: true,
      disableFetchTracking: true,
      disableExceptionTracking: true,
      disableCookiesUsage: true,
      autoTrackPageVisitTime: false,
      enableAutoRouteTracking: false,
    });
  });

  it('clamps out-of-range sampling percentages', async () => {
    stubConfigResponse({ connection_string: 'InstrumentationKey=x', sampling_percentage: 900 });
    await initBrowserTelemetry();
    expect(instances[0].config.samplingPercentage).toBe(100);
  });

  it('flushes events queued before init, with context attached', async () => {
    trackEvent('pwa.session_started');
    trackEvent('pwa.not_allowlisted');
    stubConfigResponse({ connection_string: 'InstrumentationKey=x', sampling_percentage: 100 });
    await initBrowserTelemetry();

    const [instance] = instances;
    expect(instance.events).toHaveLength(1);
    expect(instance.events[0].name).toBe('pwa.session_started');
    expect(instance.events[0].properties.flow_id).toMatch(/^[0-9a-f-]{36}$/);
    expect(instance.events[0].properties.trace_id).toMatch(/^[0-9a-f]{32}$/);
    expect(instance.events[0].properties.biller_slug).toBe('demo');
  });
});

describe('trackEvent', () => {
  it('sanitizes properties before handing them to the SDK', async () => {
    stubConfigResponse({ connection_string: 'InstrumentationKey=x', sampling_percentage: 100 });
    await initBrowserTelemetry();

    trackEvent('pwa.payment_completed', {
      method: 'ach',
      scheduled: false,
      account_number: '4421',
      payer_email: 'ada@example.com',
    });

    const [event] = instances[0].events;
    expect(event.properties.method).toBe('ach');
    expect(event.properties.scheduled).toBe('false');
    expect(event.properties).not.toHaveProperty('account_number');
    expect(event.properties).not.toHaveProperty('payer_email');
  });

  it('drops non-allowlisted events entirely', async () => {
    stubConfigResponse({ connection_string: 'InstrumentationKey=x', sampling_percentage: 100 });
    await initBrowserTelemetry();

    trackEvent('pwa.chat_message', { text: 'hello' });
    expect(instances[0].events).toHaveLength(0);
  });
});

describe('enforceAllowlist (SDK pipeline last line of defense)', () => {
  it('blocks every non-event telemetry type', () => {
    expect(enforceAllowlist({ baseType: 'PageviewData', baseData: { name: 'x' } } as never)).toBe(false);
    expect(enforceAllowlist({ baseType: 'ExceptionData', baseData: { name: 'x' } } as never)).toBe(false);
    expect(enforceAllowlist({ baseType: 'RemoteDependencyData', baseData: {} } as never)).toBe(false);
  });

  it('blocks events that bypassed trackEvent with unknown names', () => {
    expect(enforceAllowlist({ baseType: 'EventData', baseData: { name: 'rogue', properties: {} } } as never)).toBe(false);
  });

  it('re-scrubs properties and clears measurements on allowed events', () => {
    const item = {
      baseType: 'EventData',
      baseData: {
        name: 'pwa.payment_failed',
        properties: { method: 'card', error_category: 'http_5xx', receipt_id: 'PMT-99' },
        measurements: { amount: 12.34 },
      },
    };
    expect(enforceAllowlist(item as never)).toBe(true);
    expect(item.baseData.properties).toEqual({ method: 'card', error_category: 'http_5xx' });
    expect(item.baseData.measurements).toEqual({});
  });
});

describe('flowId', () => {
  it('is stable within a session and shaped like a UUID', () => {
    const first = flowId();
    expect(first).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    expect(flowId()).toBe(first);
  });

  it('persists via sessionStorage when available', () => {
    const store = new Map<string, string>();
    vi.stubGlobal('sessionStorage', {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, value),
    });
    const first = flowId();
    expect(store.get('pronto.pwa.flow_id')).toBe(first);
    resetBrowserTelemetryForTests();
    expect(flowId()).toBe(first);
  });
});
