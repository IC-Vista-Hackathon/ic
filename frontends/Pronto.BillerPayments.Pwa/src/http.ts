export class UiRequestError extends Error {
  constructor(message: string, readonly status?: number, readonly code = 'request_failed', readonly correlationId?: string, readonly retryable = false) { super(message); this.name = 'UiRequestError'; }
}
export async function fetchWithTimeout(input: RequestInfo | URL, init: RequestInit = {}, timeoutMs = 15_000): Promise<Response> {
  const controller = new AbortController(); const timeout = window.setTimeout(() => controller.abort('timeout'), timeoutMs);
  try { return await fetch(input, { ...init, signal: controller.signal }); }
  catch { if (controller.signal.aborted) throw new UiRequestError('The request timed out. Please try again.', undefined, 'request_timeout', undefined, true); throw new UiRequestError('The service could not be reached. Please try again.', undefined, 'network_error', undefined, true); }
  finally { window.clearTimeout(timeout); }
}
export async function requestError(response: Response, fallback = 'The request could not be completed.'): Promise<UiRequestError> {
  const body = await response.json().catch(() => ({})) as Record<string, any>; const nested = body.error && typeof body.error === 'object' ? body.error : {};
  const code = String(body.code ?? body.error_code ?? nested.code ?? `http_${response.status}`);
  const correlationId = response.headers.get('x-correlation-id') ?? (body.correlation_id ? String(body.correlation_id) : undefined);
  return new UiRequestError(String(body.detail ?? body.message ?? nested.message ?? fallback), response.status, code, correlationId, response.status === 408 || response.status === 429 || response.status >= 500);
}
export function errorMessage(error: unknown) { return error instanceof UiRequestError ? `${error.message}${error.correlationId ? ` Reference: ${error.correlationId}` : ''}` : error instanceof Error ? error.message : 'The request failed.'; }
