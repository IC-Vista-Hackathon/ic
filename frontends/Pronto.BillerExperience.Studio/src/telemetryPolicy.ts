import { UiRequestError } from './http';

// Strict allowlist for browser telemetry exported to Application Insights from the Biller Studio.
// Every event name and every property key AND value must match here or it is dropped before
// leaving the page. PII has no allowlisted slot by construction: biller display names, websites or
// URLs typed, chat text and agent responses, emails, any BillerExperienceDefinition content,
// biller-entered form values, amounts, and raw error messages can never pass validation because
// only fixed enums, booleans, and id-shaped strings are accepted. chat_message_sent is a count
// only — the message text itself is never a property.

export type TelemetryValue = string | number | boolean | null | undefined;
export type SanitizedEvent = { name: string; properties: Record<string, string> };

type Validator = (value: TelemetryValue) => boolean;

const oneOf = (...allowed: string[]): Validator => value => typeof value === 'string' && allowed.includes(value);
const matches = (pattern: RegExp): Validator => value => typeof value === 'string' && pattern.test(value);

const device = oneOf('desktop', 'mobile');
const outcome = oneOf('passed', 'warnings', 'failed');
const step = oneOf('vertical', 'business_location', 'brand_details', 'import_data', 'customer_experience');
const errorCategory = oneOf('network', 'http_4xx', 'http_5xx', 'unknown');

const uuid = matches(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);

// Context properties may accompany any event; shapes are pinned so nothing free-form rides along.
// biller_id is the internal tenant uuid, never a display name or slug typed by the biller.
const CONTEXT_PROPERTIES: Record<string, Validator> = {
  flow_id: uuid,
  trace_id: matches(/^[0-9a-f]{32}$/),
  biller_id: uuid,
};

const EVENTS: Record<string, Record<string, Validator>> = {
  'studio.session_started': {},
  'studio.onboarding_started': {},
  'studio.chat_message_sent': {},
  'studio.draft_generated': {},
  'studio.preview_opened': { device },
  'studio.validation_result': { outcome },
  'studio.checklist_step_completed': { step },
  'studio.publish_requested': {},
  'studio.publish_completed': {},
  'studio.publish_failed': { error_category: errorCategory },
  'studio.purchase_started': {},
  'studio.purchase_completed': {},
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
