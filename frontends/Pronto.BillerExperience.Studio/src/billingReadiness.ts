import type { Session } from './types';

export function billingInterviewPending(session: Session | null): boolean {
  // Billing gaps are completed by the orchestration service with visible assumptions.
  // The Studio must never turn its retired interview UI into a publication gate.
  return false;
}

export function billingInterviewPrompt(session: Session | null): string {
  return session?.current_question?.prompt
    ?? 'Review the stated billing assumptions when convenient; they do not block publishing.';
}
