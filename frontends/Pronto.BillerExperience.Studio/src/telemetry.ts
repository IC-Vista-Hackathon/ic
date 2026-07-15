import { randomId } from './id';

export interface ClientTrace { correlationId: string; traceparent: string }

export function newTrace(): ClientTrace {
  const traceId = randomId().replaceAll('-', '');
  const spanId = randomId().replaceAll('-', '').slice(0, 16);
  return { correlationId: randomId(), traceparent: `00-${traceId}-${spanId}-01` };
}

export function logEvent(name: string, fields: Record<string, unknown> = {}): void {
  console.info(JSON.stringify({ level: 'information', event: name, timestamp: new Date().toISOString(), ...fields }));
}

export function logError(name: string, error: unknown, fields: Record<string, unknown> = {}): void {
  const message = error instanceof Error ? error.message : String(error);
  const details = error && typeof error === 'object' ? error as Record<string, unknown> : {};
  console.error(JSON.stringify({ level: 'error', event: name, timestamp: new Date().toISOString(), message, error_type: error instanceof Error ? error.name : typeof error, error_code: details.code, http_status: details.status, correlation_id: details.correlationId ?? fields.correlation_id, retryable: details.retryable, ...fields }));
}
