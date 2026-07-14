import type { Bootstrap, ChatResponse, Deployment, ExperienceRevision } from './types';
import { logError, logEvent, newTrace } from './telemetry';

const baseUrl = import.meta.env.VITE_API_URL ?? (import.meta.env.PROD ? '/api' : 'http://localhost:5000');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const trace = newTrace();
  const started = performance.now();
  try {
    const response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: { 'content-type': 'application/json', 'x-correlation-id': trace.correlationId, traceparent: trace.traceparent, ...init?.headers },
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
    request<ChatResponse>(`/billers/${billerId}/chat`, { method: 'POST', body: JSON.stringify({ message }) }),
  approve: (billerId: string, revision: string) =>
    request<ExperienceRevision>(`/billers/${billerId}/config/approve`, { method: 'POST', body: JSON.stringify({ revision, approved_by: 'biller-studio-user' }) }),
  publish: (billerId: string, revision: string) =>
    request<Deployment>(`/billers/${billerId}/config/publish`, { method: 'POST', body: JSON.stringify({ biller_id: billerId, revision }) }),
  deployment: (billerId: string, deploymentId: string) =>
    request<Deployment>(`/billers/${billerId}/deployments/${deploymentId}`),
};
