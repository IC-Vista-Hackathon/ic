import type { AgentActivity } from './types';

export function agentActivityMeta(item: AgentActivity): string {
  const parts: string[] = [item.status];
  if (typeof item.duration_ms === 'number' && Number.isFinite(item.duration_ms)) {
    parts.push(`${Math.round(item.duration_ms)} ms`);
  }
  if (item.error_code) parts.push(item.error_code);
  return parts.join(' · ');
}
