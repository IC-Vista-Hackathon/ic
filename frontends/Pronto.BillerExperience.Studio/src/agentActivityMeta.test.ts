import { describe, expect, it } from 'vitest';
import { agentActivityMeta } from './agentActivityMeta';
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
});
