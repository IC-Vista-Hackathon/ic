import { describe, expect, it } from 'vitest';
import { billingInterviewPending, billingInterviewPrompt } from './billingReadiness';
import type { Session } from './types';

const session = (overrides: Partial<Session> = {}): Session => ({
  session_id: 'session-1', biller_id: 'biller-1', state: 'collecting_information',
  missing_fields: [], updated_at: '2026-07-15T12:00:00Z', ...overrides,
});

describe('billing publish readiness', () => {
  it('blocks publication and surfaces the exact pending confirmation question', () => {
    const pending = session({
      billing_profile: { schema_version: '1', confirmed: false, categories: [] },
      current_question: {
        question_id: 'billing.category.tax.confirmation', dimension: 'confirmation',
        prompt: 'Please confirm this billing policy: Tax: monthly, $10 late fee, pay in full', sequence: 5,
      },
    });
    expect(billingInterviewPending(pending)).toBe(true);
    expect(billingInterviewPrompt(pending)).toBe(pending.current_question?.prompt);
  });

  it('allows publication only after the billing profile is confirmed', () => {
    expect(billingInterviewPending(session({
      state: 'draft_ready',
      billing_profile: { schema_version: '1', confirmed: true, categories: [] },
      current_question: null,
    }))).toBe(false);
  });

  it('does not block legacy sessions without billing discovery state', () => {
    expect(billingInterviewPending(session())).toBe(false);
  });
});
