import { describe, expect, it } from 'vitest';
import { billingInterviewPending, billingInterviewPrompt } from './billingReadiness';
import type { Session } from './types';

const session = (overrides: Partial<Session> = {}): Session => ({
  session_id: 'session-1', biller_id: 'biller-1', state: 'collecting_information',
  missing_fields: [], updated_at: '2026-07-15T12:00:00Z', ...overrides,
});

describe('billing publish readiness', () => {
  it('does not block publication on optional confirmation', () => {
    const pending = session({
      billing_profile: { schema_version: '1', confirmed: false, categories: [] },
      current_question: {
        question_id: 'billing.category.tax.confirmation', dimension: 'confirmation',
        prompt: 'Please confirm this billing policy: Tax: monthly, $10 late fee, pay in full', sequence: 5,
      },
    });
    expect(billingInterviewPending(pending)).toBe(false);
    expect(billingInterviewPrompt(pending)).toBe(pending.current_question?.prompt);
  });

  it('does not block when an operational value will be assumed by the agents', () => {
    expect(billingInterviewPending(session({
      billing_profile: { schema_version: '1', confirmed: false, categories: [] },
      current_question: {
        question_id: 'billing.categories', dimension: 'categories',
        prompt: 'What are you billing people for?', sequence: 1,
      },
    }))).toBe(false);
  });

  it('allows publication when policy values exist without confirmation', () => {
    expect(billingInterviewPending(session({
      state: 'draft_ready',
      billing_profile: { schema_version: '1', confirmed: false, categories: [] },
      current_question: null,
    }))).toBe(false);
  });

  it('does not block legacy sessions without billing discovery state', () => {
    expect(billingInterviewPending(session())).toBe(false);
  });
});
