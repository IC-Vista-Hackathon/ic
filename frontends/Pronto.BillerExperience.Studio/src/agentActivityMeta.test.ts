import { describe, expect, it } from 'vitest';
import { agentActivityMeta, partitionAgentActivity } from './agentActivityMeta';
import type { AgentActivity } from './types';

const activity = (duration_ms: number | null | undefined): AgentActivity => ({
  event_id: 'event-1',
  sequence: 1,
  run_id: 'run-1',
  agent_id: 'biller-research',
  display_name: 'Biller Research',
  status: 'degraded',
  summary: 'Research was unavailable.',
  occurred_at: '2026-07-15T00:00:00Z',
  error_code: 'research.foundry_invalid_output',
  duration_ms,
});

describe('agentActivityMeta', () => {
  it('does not render a missing JSON duration as zero milliseconds', () => {
    expect(agentActivityMeta(activity(null))).toBe('degraded · research.foundry_invalid_output');
    expect(agentActivityMeta(activity(undefined))).toBe('degraded · research.foundry_invalid_output');
  });

  it('renders measured durations', () => {
    expect(agentActivityMeta(activity(4854.26))).toBe('degraded · 4854 ms · research.foundry_invalid_output');
  });

  it('separates non-invoked Foundry inventory from agents that executed', () => {
    const inventory: AgentActivity = {
      ...activity(null),
      event_id: 'event-inventory',
      agent_id: 'policy',
      display_name: 'Policy',
      status: 'skipped',
      summary: 'Available in the foundry inventory; not invoked (missing capability biller_research).',
      error_code: 'research.agent_ineligible',
    };
    const completed: AgentActivity = {
      ...activity(1200),
      event_id: 'event-completed',
      status: 'completed',
      summary: 'Agent returned cited research.',
      error_code: undefined,
    };

    const result = partitionAgentActivity([inventory, completed]);

    expect(result.invoked.map(item => item.agent_id)).toEqual(['biller-research']);
    expect(result.inventory.map(item => item.agent_id)).toEqual(['policy']);
  });
});
