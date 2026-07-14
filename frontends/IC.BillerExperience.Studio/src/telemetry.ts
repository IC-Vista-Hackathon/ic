export interface ClientTrace { correlationId: string; traceparent: string }

export function newTrace(): ClientTrace {
  const traceId = crypto.randomUUID().replaceAll('-', '');
  const spanId = crypto.randomUUID().replaceAll('-', '').slice(0, 16);
  return { correlationId: crypto.randomUUID(), traceparent: `00-${traceId}-${spanId}-01` };
}

export function logEvent(name: string, fields: Record<string, unknown> = {}): void {
  console.info(JSON.stringify({ level: 'information', event: name, timestamp: new Date().toISOString(), ...fields }));
}

export function logError(name: string, error: unknown, fields: Record<string, unknown> = {}): void {
  const message = error instanceof Error ? error.message : String(error);
  console.error(JSON.stringify({ level: 'error', event: name, timestamp: new Date().toISOString(), message, ...fields }));
}
