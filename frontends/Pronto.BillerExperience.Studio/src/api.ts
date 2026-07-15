import type { AgentActivitySnapshot, Bootstrap, ChatResponse, Deployment, ExperienceDefinition, ExperienceRevision, PreviewInvoice, Session } from './types';
import { logError, logEvent, newTrace } from './telemetry';
import { fetchWithTimeout, requestError } from './http';

const baseUrl = import.meta.env.VITE_API_URL ?? (import.meta.env.PROD ? '/api' : 'http://localhost:5000');
const supportingServicesBaseUrl = import.meta.env.VITE_SUPPORTING_SERVICES_URL ?? '';
// Three research workers run in bounded waves, followed by consolidation and draft generation.
// Keep the browser budget above the backend's combined agent budgets while SSE reports progress.
export const CHAT_REQUEST_TIMEOUT_MS = 120_000;
export const activityUrl = (billerId: string) => `${baseUrl}/billers/${billerId}/events`;

async function request<T>(path: string, init?: RequestInit, billerId?: string, timeoutMs?: number): Promise<T> {
  const trace = newTrace();
  const started = performance.now();
  try {
    const response = await fetchWithTimeout(`${baseUrl}${path}`, {
      ...init,
      headers: { 'content-type': 'application/json', 'x-correlation-id': trace.correlationId, traceparent: trace.traceparent, ...(billerId ? { 'x-ic-biller-id': billerId } : {}), ...init?.headers },
    }, timeoutMs);
    if (!response.ok) {
      throw await requestError(response, `Request failed with ${response.status}.`);
    }
    logEvent('studio.api.completed', { path, method: init?.method ?? 'GET', duration_ms: Math.round(performance.now() - started), correlation_id: trace.correlationId });
    return await response.json() as T;
  } catch (error) {
    logError('studio.api.failed', error, { path, method: init?.method ?? 'GET', duration_ms: Math.round(performance.now() - started), correlation_id: trace.correlationId });
    throw error;
  }
}

export interface BrandScanResult {
  outcome: 'completed' | 'degraded' | 'failed';
  primary_color: string | null;
  secondary_color: string | null;
  accent_color: string | null;
  font_family: string | null;
  logo_url: string | null;
  palette: string[];
  warnings: string[];
  error_code?: string | null;
}

export const api = {
  create: (input: { display_name: string; slug: string; bill_type: string; postal_code: string; website?: string }) =>
    request<Bootstrap>('/billers', { method: 'POST', body: JSON.stringify(input) }),
  scanBrand: (website: string) =>
    request<BrandScanResult>('/public/brand-scan', { method: 'POST', body: JSON.stringify({ website }) }),
  chat: (billerId: string, message: string, billingAnswers?: Array<{ dimension: 'categories'|'cadence'|'state_rules'|'payment_terms'; answer: string }>) =>
    request<ChatResponse>(`/billers/${billerId}/chat`, { method: 'POST', body: JSON.stringify({ message, billing_answers: billingAnswers }) }, billerId, CHAT_REQUEST_TIMEOUT_MS),
  activity: (billerId: string) => request<AgentActivitySnapshot>(`/billers/${billerId}/activity`, undefined, billerId),
  reopenBillingQuestion: (billerId: string, questionId: string) =>
    request<Session>(`/billers/${billerId}/billing-discovery/reopen`, { method: 'POST', body: JSON.stringify({ question_id: questionId }) }, billerId),
  update: (billerId: string, definition: ExperienceDefinition, expectedETag?: string) =>
    request<ExperienceRevision>(`/billers/${billerId}/config`, { method: 'PATCH', body: JSON.stringify({ definition, expected_etag: expectedETag }) }, billerId),
  invoices: (billerId: string, accountNumber = '4421') => supportingRequest<{ invoices: PreviewInvoice[] }>(
    `/invoices/billers/${encodeURIComponent(billerId)}/invoices?account_number=${encodeURIComponent(accountNumber)}&include_closed=true`, billerId),
  approve: (billerId: string, revision: string) =>
    request<ExperienceRevision>(`/billers/${billerId}/config/approve`, { method: 'POST', body: JSON.stringify({ revision, approved_by: 'biller-studio-user' }) }, billerId),
  publish: (billerId: string, revision: string) =>
    request<Deployment>(`/billers/${billerId}/config/publish`, { method: 'POST', body: JSON.stringify({ biller_id: billerId, revision }) }, billerId),
  deployment: (billerId: string, deploymentId: string) =>
    request<Deployment>(`/billers/${billerId}/deployments/${deploymentId}`, undefined, billerId),
};

async function supportingRequest<T>(path: string, billerId: string): Promise<T> {
  const trace = newTrace();
  const started = performance.now();
  try {
    const response = await fetchWithTimeout(`${supportingServicesBaseUrl}${path}`, { headers: { 'x-correlation-id': trace.correlationId, traceparent: trace.traceparent, 'x-ic-biller-id': billerId } });
    if (!response.ok) throw await requestError(response, `Supporting service request failed with ${response.status}.`);
    logEvent('studio.supporting_api.completed', { path, duration_ms: Math.round(performance.now() - started), correlation_id: trace.correlationId });
    return await response.json() as T;
  } catch (error) {
    logError('studio.supporting_api.failed', error, { path, biller_id: billerId, duration_ms: Math.round(performance.now() - started), correlation_id: trace.correlationId });
    throw error;
  }
}
