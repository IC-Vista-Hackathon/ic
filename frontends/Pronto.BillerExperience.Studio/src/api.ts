import type { AgentActivitySnapshot, Bootstrap, ChatResponse, Deployment, ExperienceDefinition, ExperienceRevision, PreviewInvoice, PreviewTenant, Session } from './types';
import { logError, logEvent, newTrace } from './telemetry';
import { fetchWithTimeout, requestError } from './http';

const baseUrl = import.meta.env.VITE_API_URL ?? (import.meta.env.PROD ? '/api' : 'http://localhost:5000');
const supportingServicesBaseUrl = import.meta.env.VITE_SUPPORTING_SERVICES_URL ?? '';
// Three research workers run in bounded waves, followed by consolidation and draft generation.
// Keep the browser budget above the backend's combined agent budgets while SSE reports progress.
export const CHAT_REQUEST_TIMEOUT_MS = 300_000;
// Approve and publish synchronously run the grounded Foundry compliance review (a full agent call,
// server budget BillerExperience:Research:AgentTimeoutSeconds, default 300s) before returning, so
// they need the same generous budget as chat — the generic 15s timeout aborts a valid review and
// surfaces a spurious "The request timed out. Please try again."
export const COMPLIANCE_GATE_TIMEOUT_MS = 300_000;
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

export const api = {
  create: (input: { display_name: string; slug: string; bill_type: string; postal_code: string; website?: string; brand?: { primary_color: string; secondary_color: string; font_family?: string } }) =>
    request<Bootstrap>('/billers', { method: 'POST', body: JSON.stringify(input) }),
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
    request<ExperienceRevision>(`/billers/${billerId}/config/approve`, { method: 'POST', body: JSON.stringify({ revision, approved_by: 'biller-studio-user' }) }, billerId, COMPLIANCE_GATE_TIMEOUT_MS),
  publish: (billerId: string, revision: string) =>
    request<Deployment>(`/billers/${billerId}/config/publish`, { method: 'POST', body: JSON.stringify({ biller_id: billerId, revision }) }, billerId, COMPLIANCE_GATE_TIMEOUT_MS),
  deployment: (billerId: string, deploymentId: string) =>
    request<Deployment>(`/billers/${billerId}/deployments/${deploymentId}`, undefined, billerId),
  // Provision (or refresh) the isolated, seeded preview tenant the built PWA renders against.
  provisionPreview: (billerId: string) =>
    request<PreviewTenant>(`/billers/${billerId}/preview`, { method: 'POST' }, billerId),
  // Wipe + deterministically re-seed the preview tenant for a fresh demo run.
  resetPreview: (billerId: string) =>
    request<PreviewTenant>(`/billers/${billerId}/preview/reset`, { method: 'POST' }, billerId),
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
