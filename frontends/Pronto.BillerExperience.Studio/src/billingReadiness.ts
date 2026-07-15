import type { Session } from './types';

export function billingInterviewPending(session: Session | null): boolean {
  if (!session) return false;
  return !!session.current_question || session.billing_profile?.confirmed === false;
}

export function billingInterviewPrompt(session: Session | null): string {
  return session?.current_question?.prompt
    ?? 'Continue the billing interview and confirm each category before publishing.';
}
