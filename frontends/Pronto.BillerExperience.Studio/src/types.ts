export type SessionState = 'collecting_information' | 'draft_ready' | 'awaiting_approval' | 'approved' | 'publishing' | 'published' | 'failed';

export interface ExperienceDefinition {
  schema_version: string;
  biller_id: string;
  brand: { display_name: string; primary_color: string; secondary_color: string; logo_asset_id: string | null; font_family: string | null };
  content: { heading: string; introduction: string; support_text: string; privacy_policy_url: string; terms_of_service_url: string };
  pwa: { name: string; short_name: string; theme_color: string; background_color: string; icon_asset_id: string | null };
  enabled_payment_capabilities: string[];
  ui?: ExperienceUi;
  preferences?: ExperiencePreferences;
}

export interface ExperienceUi { layout: string; theme: { density: string; radius: string; surface: string }; sections: Array<{ id: string; type: string; variant: string; visible: boolean }>; actions: Array<{ id: string; label: string; action: number | string; variant: string }> }
export interface ExperiencePreferences {
  guest_checkout_allowed: boolean;
  offer_autopay: boolean;
  enroll_during_payment: boolean;
  offer_paperless: boolean;
  reminder_channel: number | 'email' | 'text' | 'both' | 'none';
  accepted_methods: string[];
  self_service_history: boolean;
  self_service_updates: boolean;
  fee_handling: number | 'absorb' | 'charge' | 'mixed' | 'undecided';
  preview: { default_device: 'desktop' | 'mobile'; enabled_scenarios: PreviewScenario[] };
  recommendation_rationale?: Record<string, string>;
}
export type PreviewScenario = 'payment' | 'history' | 'communication' | 'complex';
export interface AgentActivity { event_id: string; sequence: number; run_id: string; agent_id: string; display_name: string; status: 'discovered'|'queued'|'running'|'completed'|'needs_input'|'failed'|'retrying'|'degraded'; summary: string; occurred_at: string; trace_id?: string; error_code?: string; retryable?: boolean; attempt?: number; duration_ms?: number }

export interface ExperienceRevision {
  biller_id: string;
  revision: string;
  definition: ExperienceDefinition;
  state: string;
  e_tag?: string;
  findings?: Array<{ code: string; message: string; severity: string | number; requires_review: boolean }>;
}

export interface Session {
  session_id: string;
  biller_id: string;
  state: SessionState;
  missing_fields: string[];
  updated_at: string;
}

export interface Bootstrap {
  biller: { biller_id: string; display_name: string; slug: string };
  session: Session;
  draft: ExperienceRevision;
}

export interface ChatResponse { reply: string; session: Session; draft: ExperienceRevision; generation_mode?: string }
export interface Deployment { deployment_id: string; state: string; revision: string; published_url?: string; failure_code?: string; failure_message?: string }
export interface PreviewInvoice { id: string; account_number: string; payer_name: string; description: string; amount_cents: number; due_date: string; status: 'due'|'scheduled'|'paid' }
