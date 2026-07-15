import type { AgentActivity } from './types';

export function agentActivityMeta(
  item: AgentActivity,
  status: AgentActivity['status'] = item.status,
  includeDuration = true,
): string {
  const parts: string[] = [status];
  if (includeDuration && typeof item.duration_ms === 'number' && Number.isFinite(item.duration_ms)) {
    parts.push(`${Math.round(item.duration_ms)} ms`);
  }
  if (item.error_code) parts.push(item.error_code);
  return parts.join(' · ');
}
