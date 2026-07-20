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

export function latestAgentActivity(activity: AgentActivity[]): AgentActivity[] {
  const latestRunId = [...activity]
    .sort((left, right) => left.sequence - right.sequence)
    .at(-1)?.run_id;
  return activity
    .filter(item => item.run_id === latestRunId)
    .reverse()
    .filter((item, index, all) => all.findIndex(candidate => candidate.agent_id === item.agent_id) === index)
    .reverse();
}

export function partitionAgentActivity(activity: AgentActivity[]): {
  invoked: AgentActivity[];
  inventory: AgentActivity[];
} {
  const latest = latestAgentActivity(activity);
  const inventory = latest.filter(item =>
    item.error_code === 'research.agent_ineligible' ||
    (item.status === 'discovered' && item.summary.toLowerCase().includes('not invoked')),
  );
  const inventoryIds = new Set(inventory.map(item => item.agent_id));
  return {
    invoked: latest.filter(item => !inventoryIds.has(item.agent_id)),
    inventory,
  };
}

export function shouldShowAgentId(item: Pick<AgentActivity, 'agent_id' | 'display_name'>): boolean {
  const normalize = (value: string) => value.trim().toLowerCase().replaceAll(/[-_\s]+/g, '');
  return normalize(item.agent_id) !== normalize(item.display_name);
}
