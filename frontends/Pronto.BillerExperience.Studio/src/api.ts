import type { Bootstrap, ChatResponse, Deployment, ExperienceDefinition, ExperienceRevision, PreviewInvoice } from './types';
import { logError, logEvent, newTrace } from './telemetry';

const baseUrl = import.meta.env.VITE_API_URL ?? (import.meta.env.PROD ? '/api' : 'http://localhost:5000');
const supportingServicesBaseUrl = import.meta.env.VITE_SUPPORTING_SERVICES_URL ?? '';
export const activityUrl = (billerId: string) => `${baseUrl}/billers/${billerId}/events`;

async function request<T>(path: string, init?: RequestInit, billerId?: string): Promise<T> {
  const trace = newTrace();
  const started = performance.now();
  try {
    const response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: { 'content-type': 'application/json', 'x-correlation-id': trace.correlationId, traceparent: trace.traceparent, ...(billerId ? { 'x-ic-biller-id': billerId } : {}), ...init?.headers },
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new Error(problem.detail ?? `Request failed with ${response.status}`);
    }
    logEvent('studio.api.completed', { path, method: init?.method ?? 'GET', duration_ms: Math.round(performance.now() - started), correlation_id: trace.correlationId });
    return await response.json() as T;
  } catch (error) {
    logError('studio.api.failed', error, { path, method: init?.method ?? 'GET', duration_ms: Math.round(performance.now() - started), correlation_id: trace.correlationId });
    throw error;
  }
}

export const api = {
  create: (input: { display_name: string; slug: string; bill_type: string; postal_code: string; website?: string }) =>
    request<Bootstrap>('/billers', { method: 'POST', body: JSON.stringify(input) }),
  chat: (billerId: string, message: string) =>
    request<ChatResponse>(`/billers/${billerId}/chat`, { method: 'POST', body: JSON.stringify({ message }) }, billerId),
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
  const response = await fetch(`${supportingServicesBaseUrl}${path}`, { headers: { 'x-correlation-id': trace.correlationId, traceparent: trace.traceparent, 'x-ic-biller-id': billerId } });
  if (!response.ok) throw new Error(`Supporting service request failed with ${response.status}.`);
  return response.json() as Promise<T>;
}
