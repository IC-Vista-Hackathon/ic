import { UiRequestError } from './http';

// Strict allowlist for browser telemetry exported to Application Insights. Every event name and
// every property key AND value must match here or it is dropped before leaving the page. PII has
// no allowlisted slot by construction: account numbers, payer names, emails, amounts, receipt or
// payment identifiers, chat text, and raw error messages can never pass validation because only
// fixed enums, booleans, and id-shaped strings are accepted.

export type TelemetryValue = string | number | boolean | null | undefined;
export type SanitizedEvent = { name: string; properties: Record<string, string> };

type Validator = (value: TelemetryValue) => boolean;

const boolean: Validator = value => typeof value === 'boolean';
const oneOf = (...allowed: string[]): Validator => value => typeof value === 'string' && allowed.includes(value);
const matches = (pattern: RegExp): Validator => value => typeof value === 'string' && pattern.test(value);

const method = oneOf('card', 'ach');
const errorCategory = oneOf('network', 'http_4xx', 'http_5xx', 'unknown');

// Context properties may accompany any event; shapes are pinned so nothing free-form rides along.
const CONTEXT_PROPERTIES: Record<string, Validator> = {
  flow_id: matches(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/),
  trace_id: matches(/^[0-9a-f]{32}$/),
  biller_slug: matches(/^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])$/),
};

const paymentAttempt: Record<string, Validator> = {
  method,
  scheduled: boolean,
  autopay_opt_in: boolean,
  paperless_opt_in: boolean,
};

const EVENTS: Record<string, Record<string, Validator>> = {
  'pwa.session_started': {},
  'pwa.bill_lookup': { outcome: oneOf('found', 'no_open_bill', 'failed'), error_category: errorCategory },
  'pwa.assistant_recommended': { method, scheduled: boolean },
  'pwa.assistant_asked': {},
  'pwa.assistant_pay_confirmed': { method },
  'pwa.payment_method_selected': { method },
  'pwa.review_opened': { method, scheduled: boolean },
  'pwa.payment_submitted': paymentAttempt,
  'pwa.payment_completed': paymentAttempt,
  'pwa.payment_failed': { method, scheduled: boolean, error_category: errorCategory },
  'pwa.autopay_changed': { enabled: boolean },
  'pwa.paperless_changed': { enabled: boolean },
  'pwa.preferences_saved': { outcome: oneOf('saved', 'failed'), error_category: errorCategory },
};

export const ALLOWED_EVENT_NAMES = Object.keys(EVENTS);

/**
 * Returns the event with only allowlisted, validated properties (stringified for the wire), or
 * null when the event name itself is not allowlisted. Unknown keys and invalid values are dropped
 * silently — availability of the rest of the event beats completeness.
 */
export function sanitizeEvent(name: unknown, properties: Record<string, TelemetryValue> = {}): SanitizedEvent | null {
  if (typeof name !== 'string') return null;
  const eventProperties = EVENTS[name];
  if (!eventProperties) return null;

  const sanitized: Record<string, string> = {};
  for (const [key, value] of Object.entries(properties)) {
    const validator = eventProperties[key] ?? CONTEXT_PROPERTIES[key];
    if (validator?.(value)) sanitized[key] = String(value);
  }
  return { name, properties: sanitized };
}

/** Buckets an error for export; the error's own text never leaves the browser. */
export function categorizeError(error: unknown): 'network' | 'http_4xx' | 'http_5xx' | 'unknown' {
  if (error instanceof UiRequestError) {
    if (typeof error.status === 'number') return error.status >= 500 ? 'http_5xx' : 'http_4xx';
    return error.code === 'network_error' || error.code === 'request_timeout' ? 'network' : 'unknown';
  }
  if (error instanceof TypeError) return 'network';
  return 'unknown';
}
