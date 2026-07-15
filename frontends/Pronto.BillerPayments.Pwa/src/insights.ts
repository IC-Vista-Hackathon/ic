import { ApplicationInsights, type ITelemetryItem } from '@microsoft/applicationinsights-web';
import { billerSlug } from './billerSlug';
import { randomId } from './id';
import { sharedFlow } from './telemetry';
import { sanitizeEvent, type TelemetryValue } from './telemetryPolicy';

// Application Insights browser telemetry, configured at runtime from the Biller API so the
// connection string is never baked into the frontend build. Only allowlisted semantic events
// leave the page (see telemetryPolicy.ts); the SDK's automatic collection is switched off so it
// cannot capture URLs with account data, request bodies, or raw exception text. W3C trace
// propagation to the APIs stays with the existing hand-rolled traceparent in provider.ts — the
// shared flow's trace_id rides on every event to join browser telemetry to server requests.

interface BrowserTelemetryConfiguration {
  connection_string: string | null;
  sampling_percentage: number;
}

const FLOW_STORAGE_KEY = 'pronto.pwa.flow_id';

let client: ApplicationInsights | undefined;
let initialized = false;
let inMemoryFlowId: string | undefined;
let pending: Array<{ name: string; properties: Record<string, TelemetryValue> }> = [];

/** Stable, non-PII flow id for this payer session; survives reloads within the tab session. */
export function flowId(): string {
  try {
    const existing = sessionStorage.getItem(FLOW_STORAGE_KEY);
    if (existing) return existing;
    const created = randomId();
    sessionStorage.setItem(FLOW_STORAGE_KEY, created);
    return created;
  } catch {
    inMemoryFlowId ??= randomId();
    return inMemoryFlowId;
  }
}

/**
 * Fetches the runtime telemetry configuration and starts the browser client when a connection
 * string is configured. Resolves false (and leaves telemetry disabled) on any failure — payers
 * never see telemetry errors.
 */
export async function initBrowserTelemetry(configUrl = '/api/public/telemetry'): Promise<boolean> {
  try {
    const response = await fetch(configUrl);
    if (!response.ok) return finishInit(undefined);
    const configuration = (await response.json()) as BrowserTelemetryConfiguration;
    if (!configuration.connection_string) return finishInit(undefined);

    const created = new ApplicationInsights({
      config: {
        connectionString: configuration.connection_string,
        samplingPercentage: clampPercentage(configuration.sampling_percentage),
        // Automatic collection stays off: fetch/ajax tracking would duplicate the hand-rolled
        // traceparent headers and record query strings; exception tracking would export raw
        // error text; cookies are unnecessary because flow_id already scopes the session.
        disableAjaxTracking: true,
        disableFetchTracking: true,
        disableExceptionTracking: true,
        disableCookiesUsage: true,
        enableUnhandledPromiseRejectionTracking: false,
        autoTrackPageVisitTime: false,
        enableAutoRouteTracking: false,
        loggingLevelConsole: 0,
        loggingLevelTelemetry: 0,
      },
    });
    created.loadAppInsights();
    created.addTelemetryInitializer(enforceAllowlist);
    return finishInit(created);
  } catch {
    return finishInit(undefined);
  }
}

/** Emits an allowlisted semantic event; anything else is dropped. Safe to call before init. */
export function trackEvent(name: string, properties: Record<string, TelemetryValue> = {}): void {
  if (!initialized) {
    pending.push({ name, properties });
    return;
  }
  if (!client) return;
  const sanitized = sanitizeEvent(name, { ...contextProperties(), ...properties });
  if (!sanitized) return;
  client.trackEvent({ name: sanitized.name }, sanitized.properties);
}

/** Last line of defense inside the SDK pipeline: only sanitized custom events may leave. */
export function enforceAllowlist(item: ITelemetryItem): boolean {
  if (item.baseType !== 'EventData') return false;
  const sanitized = sanitizeEvent(item.baseData?.name, item.baseData?.properties ?? {});
  if (!sanitized || !item.baseData) return false;
  item.baseData.properties = sanitized.properties;
  item.baseData.measurements = {};
  return true;
}

/** Test seam: resets module state between runs. */
export function resetBrowserTelemetryForTests(): void {
  client = undefined;
  initialized = false;
  inMemoryFlowId = undefined;
  pending = [];
}

function contextProperties(): Record<string, TelemetryValue> {
  return { flow_id: flowId(), trace_id: sharedFlow.traceId, biller_slug: billerSlug() };
}

function finishInit(created: ApplicationInsights | undefined): boolean {
  client = created;
  initialized = true;
  const queued = pending;
  pending = [];
  for (const event of queued) trackEvent(event.name, event.properties);
  return client !== undefined;
}

function clampPercentage(value: number): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 100;
}
