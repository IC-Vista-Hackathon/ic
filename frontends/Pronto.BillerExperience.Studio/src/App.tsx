import { CSSProperties, FormEvent, useEffect, useRef, useState } from 'react';
import { activityUrl, api } from './api';
import { errorMessage, UiRequestError, type ValidationFinding } from './http';
import { toBillerSlug } from './slug';
import { trackEvent } from './insights';
import { categorizeError } from './telemetryPolicy';
import { logError, logEvent } from './telemetry';
import { agentActivityMeta, partitionAgentActivity, shouldShowAgentId } from './agentActivityMeta';
import { billingInterviewPending, billingInterviewPrompt } from './billingReadiness';
import type { AgentActivity, Deployment, ExperienceDefinition, ExperienceRevision, Session } from './types';

const PUBLISH_FAILURE_MESSAGE = 'We could not publish your payer site. Please try again. If the problem continues, contact support.';

/* ------------------------------------------------------------------ *
 * Pronto Payment Portal Builder — Biller Experience Studio
 *
 * A faithful React port of the "Payment Experience Builder" design
 * output (Claude Design + Claude Code). Guided, client-side demo that
 * walks a biller from landing -> wizard -> analysis -> review -> live
 * payer preview -> pricing -> dashboard. Money movement stays a
 * deterministic service responsibility; this surface is presentation.
 * ------------------------------------------------------------------ */

const asset = (p: string) => `${import.meta.env.BASE_URL}${p}`;

// Allowlisted step enum for studio.checklist_step_completed, indexed by wizard step.
const CHECKLIST_STEPS = ['vertical', 'business_location', 'brand_details', 'import_data', 'customer_experience'] as const;

// Maps a generated draft's validation findings to the allowlisted outcome enum. Only the outcome
// bucket leaves the page — finding codes, messages, and severities never become telemetry.
function validationOutcome(revision: ExperienceRevision | null): 'passed' | 'warnings' | 'failed' {
  const findings = revision?.findings ?? [];
  if (!findings.length) return 'passed';
  const isBlocking = (severity: string | number) =>
    typeof severity === 'number' ? severity >= 2 : ['high', 'error', 'critical', 'blocker'].includes(severity.toLowerCase());
  return findings.some(f => f.requires_review || isBlocking(f.severity)) ? 'failed' : 'warnings';
}

// Agent statuses a run can legitimately settle into. Anything else (running, queued, discovered,
// needs_input, retrying) is still in-flight and must not persist once the run has finished.
const TERMINAL_AGENT_STATUSES: AgentActivity['status'][] = ['completed', 'failed', 'degraded', 'skipped'];
const isTerminalStatus = (status: AgentActivity['status']) => TERMINAL_AGENT_STATUSES.includes(status);

// Worst-case outcome of a finished run, used to keep the completion header honest. A run is only
// "success" when nothing failed, nothing degraded, and no agent was left stranded in a
// non-terminal state; otherwise it is reported as warnings/failed rather than success.
function runOutcome(activity: AgentActivity[]): 'success' | 'warnings' | 'failed' {
  // Collapse the accumulated event history to the latest event per agent; the raw array keeps
  // superseded statuses (discovered -> running -> completed) that would otherwise read as in-flight.
  const { invoked } = partitionAgentActivity(activity);
  if (invoked.some(item => item.status === 'failed')) return 'failed';
  if (invoked.some(item => item.status === 'degraded' || !isTerminalStatus(item.status))) return 'warnings';
  return 'success';
}

interface ProposedChange { field: string; detail: string }

// Derives the "proposed revision" summary strictly from the diff between the current definition and
// the one the agent proposes, so the card lists exactly what "Accept and update preview" will apply
// — never a field that didn't change. If it returns [], nothing the preview can honor changed.
function proposedChanges(current: ExperienceDefinition | null | undefined, proposed: ExperienceDefinition): ProposedChange[] {
  if (!current) return [];
  const changes: ProposedChange[] = [];
  if (current.brand.primary_color.toLowerCase() !== proposed.brand.primary_color.toLowerCase()) {
    changes.push({ field: 'Primary color', detail: `${current.brand.primary_color} → ${proposed.brand.primary_color}` });
  }
  if (current.content.heading !== proposed.content.heading) {
    changes.push({ field: 'Heading', detail: `“${proposed.content.heading}”` });
  }
  const currentLabel = current.ui?.actions?.[0]?.label;
  const proposedLabel = proposed.ui?.actions?.[0]?.label;
  if (proposedLabel && currentLabel !== proposedLabel) {
    changes.push({ field: 'Primary action', detail: `“${proposedLabel}”` });
  }
  const currentMethods = [...(current.preferences?.accepted_methods ?? [])].sort();
  const proposedMethods = [...(proposed.preferences?.accepted_methods ?? [])].sort();
  if (currentMethods.join(',') !== proposedMethods.join(',')) {
    changes.push({ field: 'Payment methods', detail: proposedMethods.join(', ') || 'none' });
  }
  const toggles = [
    ['offer_autopay', 'AutoPay'],
    ['offer_paperless', 'Paperless billing'],
    ['guest_checkout_allowed', 'Guest checkout'],
  ] as const;
  for (const [key, label] of toggles) {
    const after = proposed.preferences?.[key];
    if (typeof after === 'boolean' && current.preferences?.[key] !== after) {
      changes.push({ field: label, detail: after ? 'enabled' : 'disabled' });
    }
  }
  return changes;
}

/** Parse a CSS declaration string into a React style object so the
 *  design's inline styles can be transcribed verbatim. */
function css(text: string): CSSProperties {
  const style: Record<string, string> = {};
  for (const part of text.split(';')) {
    const idx = part.indexOf(':');
    if (idx === -1) continue;
    const prop = part.slice(0, idx).trim();
    const value = part.slice(idx + 1).trim();
    if (!prop) continue;
    const jsProp = prop.replace(/-([a-z])/g, (_m, c: string) => c.toUpperCase());
    style[jsProp] = value;
  }
  return style as CSSProperties;
}

function paymentTermsLabel(mode: string | number | null | undefined, maximum?: number | null): string {
  const installments = mode === 'installments_allowed' || mode === 1;
  if (!installments) return 'Pay in full';
  return maximum ? `Up to ${maximum} installments` : 'Installments available';
}

type VerticalId = 'insurance' | 'utilities' | 'tax' | 'other';
type MethodType = 'card' | 'bank' | 'applepay' | 'googlepay' | 'paypal';

interface Vertical { id: VerticalId; label: string; icon: string; desc: string; }
interface Palette { primary: string; secondary: string; accent: string; font: string; }
interface Brand extends Palette { initials: string; colorsFromLogo?: boolean; }
interface Category { label: string; text: string; }
interface Compliance { states: string[]; byState: Record<string, Category[]>; }
interface Breakdown { key: string; amount: number; }
interface Statement { id: string; period: string; date: string; due: string; status: string; amount: number; breakdown: Breakdown[]; type?: string; statusColor?: 'green' | 'yellow'; note?: string; noteEmphasis?: boolean; }
interface ScenarioResult { title: string; intent: string; lines: string[]; }
interface ImportedField { label: string; value: string; }

interface Lob {
  id: string;
  vertical: VerticalId | null;
  bizName: string;
  selectedStates: string[];
  website: string;
  skipWebsite: boolean;
  brand: Brand | null;
  compliance: Compliance | null;
  published: boolean;
  docs: string[];
  logoDataUrl: string | null;
  accountNumber: string | null;
  guestCheckoutAllowed: boolean;
  offerAutopay: boolean;
  enrollDuringPayment: boolean;
  offerPaperless: boolean;
  reminderChannel: string;
  acceptedMethods: string[];
  selfServiceHistory: boolean;
  selfServiceUpdate: boolean;
  feeHandling: string;
  backendBillerId: string | null;
  backendDraft: ExperienceRevision | null;
  backendSession: Session | null;
  deployment: Deployment | null;
}

type Screen = 'landing' | 'wizard' | 'analyzing' | 'results' | 'preview' | 'dashboard';
type DashboardSection = 'home' | 'lob' | 'billing' | 'settings' | 'help';

interface State {
  screen: Screen;
  dashboardSection: DashboardSection;
  wizardStep: number;
  vertical: VerticalId | null;
  otherVerticalDescription: string;
  bizName: string;
  selectedStates: string[];
  stateSearch: string;
  website: string;
  skipWebsite: boolean;
  brand: Brand | null;
  compliance: Compliance | null;
  payerStep: number;
  amount: number;
  methodType: MethodType;
  autopayOptIn: boolean;
  paperlessOptIn: boolean;
  processing: boolean;
  analyzeStage: number;
  modal: 'signup' | 'checkout' | null;
  purchased: boolean;
  accountCreated: boolean;
  billerAccountEmail: string | null;
  signupEmail: string;
  signupPassword: string;
  signupError: string | null;
  lobs: Lob[];
  editingLobId: string | null;
  pendingLob: boolean;
  viewingStatementId: string | null;
  agreedToCompliance: boolean;
  docs: string[];
  newDocName: string;
  expandedCompliance: string[];
  logoDataUrl: string | null;
  logoFetchOk: boolean;
  extractedColors: string[] | null;
  colorChoice: 'auto' | 'custom';
  customPrimary: string;
  customSecondary: string;
  customAccent: string;
  fontChoice: string;
  guestCheckoutAllowed: boolean;
  offerAutopay: boolean;
  enrollDuringPayment: boolean;
  offerPaperless: boolean;
  reminderChannel: string;
  acceptedMethods: string[];
  selfServiceHistory: boolean;
  selfServiceUpdate: boolean;
  feeHandling: string;
  setupPath: 'upload' | 'manual' | null;
  chatAnswers: string[];
  chatStep: number;
  chatDraft: string;
  chatActive: boolean;
  analyzingUpload: boolean;
  editingChatIndex: number | null;
  aiApplied: boolean;
  aiRationale: Record<string, string>;
  editingSection: string | null;
  reviewEditingSection: string | null;
  reviewSaveError: boolean;
  csvFileName: string | null;
  importedFields: ImportedField[];
  csvOverriddenFields: string[];
  accountNumber: string | null;
  previewScenario: string;
  complexScenarioText: string;
  complexScenarioResult: ScenarioResult | null;
  scenarioLoading: boolean;
  previewDevice: 'desktop' | 'mobile';
  statementTab: 'current' | 'past';
  accountEmail: string;
  accountPassword: string;
  previewAutopayEnrolled: boolean;
  previewAutopayEnrolling: boolean;
  previewAutopaySource: 'existing' | 'new';
  previewAutopayMethodType: 'card' | 'bank';
  previewPaperlessEnrolled: boolean;
  payCardNumber: string;
  payCardExpiry: string;
  payCardCvc: string;
  payBankRouting: string;
  payBankAccount: string;
  payError: string | null;
  backendBillerId: string | null;
  backendDraft: ExperienceRevision | null;
  backendSession: Session | null;
  deployment: Deployment | null;
  publishing: boolean;
  publishError: { message: string; findings: ValidationFinding[]; reference?: string } | null;
  agentActivity: AgentActivity[];
  activityConnection: 'idle' | 'connecting' | 'connected' | 'disconnected';
  orchestrationError: string | null;
  analysisComplete: boolean;
  previewChatInput: string;
  previewChatBusy: boolean;
  previewChatError: string | null;
  previewProposal: ExperienceRevision | null;
  previewChatReply: string | null;
  previewGenerationMode: string | null;
}

const VERTICALS: Vertical[] = [
  { id: 'insurance', label: 'Insurance', icon: 'DocumentSearch', desc: 'Premium billing, claims payments, policy renewals' },
  { id: 'utilities', label: 'Utilities', icon: 'Bolt', desc: 'Electric, water, gas, and municipal utility billing' },
  { id: 'tax', label: 'Government / Taxes', icon: 'Invoice', desc: 'Property tax, court fees, and municipal payments' },
  { id: 'other', label: 'Other', icon: 'Question', desc: 'Describe your business and we\u2019ll tailor suggestions to it' },
];

const PALETTES: Palette[] = [
  { primary: '#0B4F6C', secondary: '#01BAEF', accent: '#EAF7FB', font: 'Poppins' },
  { primary: '#1B4332', secondary: '#40916C', accent: '#E7F5EC', font: 'Work Sans' },
  { primary: '#6A0F0F', secondary: '#B23A48', accent: '#FBEDE7', font: 'Source Sans Pro' },
  { primary: '#2B2D42', secondary: '#8D99AE', accent: '#EDF2F4', font: 'Montserrat' },
  { primary: '#3A0CA3', secondary: '#7209B7', accent: '#F1EAFB', font: 'Nunito Sans' },
  { primary: '#0D3B66', secondary: '#F4A261', accent: '#FCEFDF', font: 'Lato' },
];

const GOOGLE_FONTS = ['Poppins', 'Montserrat', 'Source Sans Pro', 'Nunito Sans', 'Work Sans', 'Lato', 'Open Sans', 'Roboto Slab'];

// Convenience/service fee charged to the payer when the biller passes processing costs through
// (feeHandling === 'charge'). Absorbed, mixed, and undecided all show no payer-facing fee.
const SERVICE_FEE = 2.5;

const VERTICAL_DOC_LABELS: Record<VerticalId, { docLabel: string; numberPrefix: string; numberLabel: string; personLabel: string; periodLabel: string; totalLabel: string; issuerLabel: string; }> = {
  insurance: { docLabel: 'Policy Statement', numberPrefix: 'POL-', numberLabel: 'Policy Number', personLabel: 'Named Insured', periodLabel: 'Coverage Period', totalLabel: 'Premium Due', issuerLabel: 'Carrier' },
  utilities: { docLabel: 'Utility Bill', numberPrefix: 'ACCT-', numberLabel: 'Account Number', personLabel: 'Account Holder', periodLabel: 'Billing Period', totalLabel: 'Amount Due', issuerLabel: 'Utility Provider' },
  tax: { docLabel: 'Tax Statement', numberPrefix: 'PCL-', numberLabel: 'Parcel Number', personLabel: 'Taxpayer', periodLabel: 'Tax Period', totalLabel: 'Amount Due', issuerLabel: 'Taxing Authority' },
  other: { docLabel: 'Statement', numberPrefix: 'ACCT-', numberLabel: 'Account Number', personLabel: 'Account Holder', periodLabel: 'Billing Period', totalLabel: 'Amount Due', issuerLabel: 'Biller' },
};

const LINEITEM_LABELS: Record<VerticalId, { base: string; usage: string; surcharge: string; }> = {
  insurance: { base: 'Base Premium', usage: 'Endorsement Adjustment', surcharge: 'Policy Fee' },
  utilities: { base: 'Base Charge', usage: 'Usage Adjustment', surcharge: 'Municipal Surcharge' },
  tax: { base: 'Base Assessment', usage: 'Special Assessment', surcharge: 'Administrative Fee' },
  other: { base: 'Base Charge', usage: 'Adjustment', surcharge: 'Service Fee' },
};

const STATEMENTS: Statement[] = [
  { id: '240183', period: 'Jun 4 - Jul 3', date: 'Posted Jul 1', due: 'Due Aug 4', status: 'Due', amount: 128.42, breakdown: [{ key: 'base', amount: 98.0 }, { key: 'usage', amount: 22.42 }, { key: 'surcharge', amount: 8.0 }] },
  { id: '239022', period: 'May 4 - Jun 3', date: 'Posted Jun 1', due: 'Paid Jun 3', status: 'Paid', amount: 112.15, breakdown: [{ key: 'base', amount: 96.0 }, { key: 'usage', amount: 16.15 }] },
];

// Curated demo statement sets keyed off the biller vertical, mirroring the Invoice API's
// FakeInvoiceFactory seed. These make the payer preview show the real demo invoices (type +
// green/yellow status + notes) instead of the generic placeholder set. Verticals without an
// entry fall back to STATEMENTS.
const VERTICAL_STATEMENTS: Partial<Record<VerticalId, Statement[]>> = {
  insurance: [
    { id: '778120', type: 'Auto', period: 'Jul 14, 2026 - Jul 14, 2027', date: 'Due Jul 14, 2026', due: 'Coverage Period', status: 'Due', amount: 142.5, statusColor: 'yellow',
      note: 'Overdue but in the grace period — pay today to keep your policy active with no penalty.', noteEmphasis: true,
      breakdown: [{ key: 'base', amount: 130.0 }, { key: 'surcharge', amount: 12.5 }] },
    { id: '640318', type: 'Home', period: 'Aug 30, 2026 - Aug 30, 2027', date: 'Due Aug 30, 2026', due: 'Coverage Period', status: 'Due', amount: 89.0, statusColor: 'green',
      breakdown: [{ key: 'base', amount: 82.0 }, { key: 'surcharge', amount: 7.0 }] },
    { id: '905513', type: 'Life', period: 'Dec 31, 2026 - Dec 31, 2027', date: 'Due Dec 31, 2026', due: 'Coverage Period', status: 'Due', amount: 45.0, statusColor: 'green',
      breakdown: [{ key: 'base', amount: 45.0 }] },
  ],
  other: [
    { id: '100234', type: 'HOA Dues', period: 'Q3 2026', date: 'Due Jul 31, 2026', due: 'Billing Period', status: 'Due', amount: 350.0, statusColor: 'green',
      breakdown: [{ key: 'base', amount: 350.0 }] },
    { id: '100235', type: 'Special Assessment (Pool)', period: 'One-time assessment', date: 'Due Dec 31, 2026', due: 'Billing Period', status: 'Due', amount: 4500.0, statusColor: 'green',
      note: 'This assessment is much larger than your other bills — a payment plan is recommended.', noteEmphasis: true,
      breakdown: [{ key: 'base', amount: 4500.0 }] },
    { id: '100236', type: 'HOA Fine', period: 'One-time fine', date: 'Due Jul 31, 2026', due: 'Billing Period', status: 'Due', amount: 100.0, statusColor: 'green',
      note: '$100 fine for playing "All I Want for Christmas is You" during summer.',
      breakdown: [{ key: 'base', amount: 100.0 }] },
  ],
};

const CHAT_QUESTIONS = [
  'What are you billing people for? (the line items / categories)',
  'How often is each one billed? (cadence per category)',
  'What are the rules that decide when a payment is late or a policy/account changes state?',
  'Can any of these be paid over time, or is it pay-in-full?',
];

const CHAT_RATIONALES = [
  'We use this to define your statement line items.',
  'This sets your default reminder and billing cadence.',
  'This informs late-fee and status-change logic in your compliance rules.',
  'This determines whether we offer installment plans at checkout.',
];

// The onboarding chat asks four questions that map, in order, to the backend billing
// dimensions [categories, cadence, state_rules, payment_terms]. The wording and the AI-suggested
// starting answer are tailored to the vertical picked in step one, so the agent proposes a
// business setup the biller can accept or edit. CHAT_QUESTIONS is the generic/"other" fallback.
const VERTICAL_QUESTIONS: Record<VerticalId, string[]> = {
  insurance: [
    'What types of policies are you billing premiums for?',
    'How often is each policy billed?',
    'What happens when a premium payment is late?',
    'Can premiums be paid in installments, or in full each term?',
  ],
  utilities: [
    'Which utility services are you billing for?',
    'How often is each service billed?',
    'What are your late-payment and shut-off rules?',
    'Can past-due balances be paid over time, or is it pay-in-full?',
  ],
  tax: [
    'Which taxes or government fees are you collecting?',
    'How often is each one assessed?',
    'What penalties or interest apply when a payment is late?',
    'Can these be paid on an installment plan, or in full?',
  ],
  other: CHAT_QUESTIONS,
};

const VERTICAL_SUGGESTIONS: Record<VerticalId, string[]> = {
  insurance: [
    'Auto, home, and life insurance premiums',
    'Monthly premiums, with annual policy renewals',
    'A grace period applies, then the policy lapses if it stays unpaid',
    'Both — monthly installments or pay-in-full per term',
  ],
  utilities: [
    'Electric, water, gas, and sewer/municipal services',
    'Monthly, based on metered usage',
    'A late fee after the due date, with a disconnection notice at 30 days past due',
    'Payment plans for past-due balances; current bills are pay-in-full',
  ],
  tax: [
    'Property tax, court fees, and municipal permit fees',
    'Property tax annually or semi-annually; other fees as they are incurred',
    'Interest accrues monthly on delinquent balances, with liens after prolonged non-payment',
    'Installment plans for property tax; other fees are pay-in-full',
  ],
  other: [
    'Dues, fees, and one-time charges',
    'Monthly recurring, with some one-time items',
    'A late fee applies after the due date',
    'Both installments and pay-in-full are available',
  ],
};

// Pronto wordmark path data (from public/assets/pronto-logo.svg), inlined so the analyzing
// screen can draw the outline and then fill the logo right-to-left as the preview builds.
const PRONTO_PATHS = [
  'M360.541 103.104C351.997 103.104 345.277 100.8 340.381 96.192C335.485 91.488 333.037 85.152 333.037 77.184C333.037 76.224 333.133 74.592 333.325 72.288L338.509 30.816C339.661 21.408 343.357 13.92 349.597 8.35199C355.933 2.784 363.805 0 373.213 0C381.661 0 388.333 2.352 393.229 7.05599C398.221 11.664 400.717 17.952 400.717 25.92C400.717 26.88 400.621 28.512 400.429 30.816L395.389 72.288C394.237 81.696 390.493 89.184 384.157 94.752C377.821 100.32 369.949 103.104 360.541 103.104ZM363.421 79.92C364.861 79.92 366.061 79.344 367.021 78.192C368.077 77.04 368.749 75.456 369.037 73.44L374.365 29.664C374.557 27.648 374.317 26.064 373.645 24.912C372.973 23.76 371.917 23.184 370.477 23.184C368.941 23.184 367.645 23.76 366.589 24.912C365.629 26.064 365.053 27.648 364.861 29.664L359.533 73.44C359.437 73.824 359.389 74.352 359.389 75.024C359.389 76.56 359.725 77.76 360.397 78.624C361.165 79.488 362.173 79.92 363.421 79.92Z',
  'M339.138 1.15186C339.714 1.15186 340.146 1.34385 340.434 1.72786C340.818 2.11186 340.962 2.63986 340.866 3.31185L338.562 22.1759C338.466 22.8479 338.226 23.3759 337.842 23.7599C337.458 24.1439 336.93 24.3359 336.258 24.3359H319.554C319.074 24.3359 318.834 24.5759 318.834 25.0559L309.618 99.7919C309.522 100.464 309.282 100.992 308.898 101.376C308.514 101.76 307.986 101.952 307.314 101.952H285.282C284.61 101.952 284.082 101.76 283.698 101.376C283.41 100.992 283.314 100.464 283.41 99.7919L292.626 25.0559C292.626 24.5759 292.386 24.3359 291.906 24.3359H275.778C275.106 24.3359 274.578 24.1439 274.194 23.7599C273.906 23.3759 273.81 22.8479 273.906 22.1759L276.21 3.31185C276.306 2.63986 276.546 2.11186 276.93 1.72786C277.41 1.34385 277.986 1.15186 278.658 1.15186H339.138Z',
  'M247.018 3.31185C247.114 2.63986 247.354 2.11186 247.738 1.72786C248.218 1.34385 248.794 1.15186 249.466 1.15186H271.21C271.882 1.15186 272.362 1.34385 272.65 1.72786C273.034 2.11186 273.178 2.63986 273.082 3.31185L261.13 99.7919C261.034 100.464 260.794 100.992 260.41 101.376C260.026 101.76 259.498 101.952 258.826 101.952H233.914C232.762 101.952 232.09 101.328 231.898 100.08L226.426 55.8719C226.426 55.5839 226.33 55.4399 226.138 55.4399C225.946 55.4399 225.85 55.5839 225.85 55.8719L220.666 99.7919C220.57 100.464 220.282 100.992 219.802 101.376C219.418 101.76 218.938 101.952 218.362 101.952H196.474C195.898 101.952 195.418 101.76 195.034 101.376C194.746 100.992 194.65 100.464 194.746 99.7919L206.554 3.31185C206.65 2.63986 206.89 2.11186 207.274 1.72786C207.754 1.34385 208.282 1.15186 208.858 1.15186H233.482C234.634 1.15186 235.306 1.77586 235.498 3.02386L241.114 47.5199C241.114 47.8079 241.21 47.9039 241.402 47.8079C241.594 47.7119 241.738 47.5679 241.834 47.3759L247.018 3.31185Z',
  'M159.79 103.104C151.246 103.104 144.526 100.8 139.63 96.192C134.734 91.488 132.286 85.152 132.286 77.184C132.286 76.224 132.382 74.592 132.574 72.288L137.758 30.816C138.91 21.408 142.606 13.92 148.846 8.35199C155.182 2.784 163.054 0 172.462 0C180.91 0 187.582 2.352 192.478 7.05599C197.47 11.664 199.966 17.952 199.966 25.92C199.966 26.88 199.87 28.512 199.678 30.816L194.638 72.288C193.486 81.696 189.742 89.184 183.406 94.752C177.07 100.32 169.198 103.104 159.79 103.104ZM162.67 79.92C164.11 79.92 165.31 79.344 166.27 78.192C167.326 77.04 167.998 75.456 168.286 73.44L173.614 29.664C173.806 27.648 173.566 26.064 172.894 24.912C172.222 23.76 171.166 23.184 169.726 23.184C168.19 23.184 166.894 23.76 165.838 24.912C164.878 26.064 164.302 27.648 164.11 29.664L158.782 73.44C158.686 73.824 158.638 74.352 158.638 75.024C158.638 76.56 158.974 77.76 159.646 78.624C160.414 79.488 161.422 79.92 162.67 79.92Z',
  'M102.535 101.952C101.383 101.952 100.711 101.328 100.519 100.08L96.343 63.7919C96.343 63.4079 96.199 63.2159 95.911 63.2159C95.527 63.2159 95.335 63.4559 95.335 63.9359L90.871 99.7919C90.775 100.464 90.487 100.992 90.007 101.376C89.623 101.76 89.143 101.952 88.567 101.952H66.3909C65.8149 101.952 65.335 101.76 64.951 101.376C64.663 100.992 64.567 100.464 64.663 99.7919L76.471 3.31185C76.567 2.63986 76.807 2.11186 77.191 1.72786C77.671 1.34385 78.199 1.15186 78.775 1.15186H110.311C117.991 1.15186 124.039 3.59986 128.455 8.49586C132.967 13.3919 135.223 19.8719 135.223 27.9359C135.223 30.2399 135.127 32.0159 134.935 33.2639C134.263 38.5439 132.775 43.2959 130.471 47.5199C128.167 51.6479 125.191 55.0079 121.543 57.5999C121.063 57.8879 120.919 58.2239 121.111 58.6079L127.303 99.5039V100.08C127.303 100.656 127.111 101.136 126.727 101.52C126.343 101.808 125.863 101.952 125.287 101.952H102.535ZM100.807 24.3359C100.327 24.3359 100.087 24.5759 100.087 25.0559L97.927 41.9039C97.927 42.3839 98.167 42.6239 98.647 42.6239H100.519C102.823 42.6239 104.743 41.6159 106.279 39.5999C107.911 37.5839 108.727 34.7999 108.727 31.2479C108.727 29.0399 108.199 27.3599 107.143 26.2079C106.087 24.9599 104.647 24.3359 102.823 24.3359H100.807Z',
  'M45.684 1.15186C53.364 1.15186 59.412 3.64786 63.828 8.63986C68.34 13.5359 70.596 20.0639 70.596 28.2239C70.596 30.5279 70.5 32.3039 70.308 33.5519C69.54 39.6959 67.716 45.1199 64.836 49.8239C62.052 54.5279 58.404 58.1759 53.892 60.7679C49.476 63.2639 44.58 64.5119 39.204 64.5119H31.284C30.804 64.5119 30.564 64.7519 30.564 65.2319L26.244 99.7919C26.148 100.464 25.86 100.992 25.38 101.376C24.996 101.76 24.516 101.952 23.94 101.952H1.764C1.188 101.952 0.708 101.76 0.324 101.376C0.036 100.992 -0.06 100.464 0.036 99.7919L11.844 3.31185C11.94 2.63986 12.18 2.11186 12.564 1.72786C13.044 1.34385 13.572 1.15186 14.148 1.15186H45.684ZM35.748 43.6319C37.86 43.6319 39.636 42.8639 41.076 41.3279C42.612 39.6959 43.572 37.2959 43.956 34.1279C44.052 33.5519 44.1 32.7359 44.1 31.6799C44.1 29.2799 43.572 27.4559 42.516 26.2079C41.46 24.9599 40.02 24.3359 38.196 24.3359H36.18C35.7 24.3359 35.46 24.5759 35.46 25.0559L33.3 42.9119C33.108 43.3919 33.3 43.6319 33.876 43.6319H35.748Z',
];

const STATE_OPTIONS = ['Alabama', 'Alaska', 'Arizona', 'Arkansas', 'California', 'Colorado', 'Connecticut', 'Delaware', 'Florida', 'Georgia', 'Hawaii', 'Idaho', 'Illinois', 'Indiana', 'Iowa', 'Kansas', 'Kentucky', 'Louisiana', 'Maine', 'Maryland', 'Massachusetts', 'Michigan', 'Minnesota', 'Mississippi', 'Missouri', 'Montana', 'Nebraska', 'Nevada', 'New Hampshire', 'New Jersey', 'New Mexico', 'New York', 'North Carolina', 'North Dakota', 'Ohio', 'Oklahoma', 'Oregon', 'Pennsylvania', 'Rhode Island', 'South Carolina', 'South Dakota', 'Tennessee', 'Texas', 'Utah', 'Vermont', 'Virginia', 'Washington', 'West Virginia', 'Wisconsin', 'Wyoming'];

// Representative postal code per operating state, used to seed the biller record so downstream
// compliance/jurisdiction checks reflect where the biller actually operates instead of a fixed
// out-of-state default.
const STATE_POSTAL_CODE: Record<string, string> = {
  Alabama: '35203', Alaska: '99501', Arizona: '85001', Arkansas: '72201', California: '94103',
  Colorado: '80202', Connecticut: '06103', Delaware: '19801', Florida: '33101', Georgia: '30303',
  Hawaii: '96813', Idaho: '83702', Illinois: '60601', Indiana: '46204', Iowa: '50309',
  Kansas: '66603', Kentucky: '40202', Louisiana: '70112', Maine: '04101', Maryland: '21201',
  Massachusetts: '02108', Michigan: '48226', Minnesota: '55401', Mississippi: '39201', Missouri: '63101',
  Montana: '59601', Nebraska: '68102', Nevada: '89101', 'New Hampshire': '03301', 'New Jersey': '07102',
  'New Mexico': '87501', 'New York': '10001', 'North Carolina': '27601', 'North Dakota': '58501', Ohio: '43215',
  Oklahoma: '73102', Oregon: '97201', Pennsylvania: '19103', 'Rhode Island': '02903', 'South Carolina': '29201',
  'South Dakota': '57501', Tennessee: '37219', Texas: '78701', Utah: '84101', Vermont: '05601',
  Virginia: '23219', Washington: '98101', 'West Virginia': '25301', Wisconsin: '53703', Wyoming: '82001',
};

/** Lazily load a Google font for the biller's chosen brand font so the
 *  payer preview renders in that typeface. */
let _lastFontLoaded = '';
function ensureFontLoaded(fontName: string): void {
  if (typeof document === 'undefined') return;
  if (!fontName || fontName === 'Arial' || fontName === 'Inter') return;
  if (_lastFontLoaded === fontName) return;
  _lastFontLoaded = fontName;
  const id = 'dynamic-brand-font';
  const href = `https://fonts.googleapis.com/css2?family=${encodeURIComponent(fontName).replace(/%20/g, '+')}:wght@300;400;500;700&display=swap`;
  let link = document.getElementById(id) as HTMLLinkElement | null;
  if (link) { if (link.getAttribute('href') !== href) link.setAttribute('href', href); return; }
  link = document.createElement('link');
  link.id = id; link.rel = 'stylesheet'; link.href = href;
  document.head.appendChild(link);
}

/** Strip CSS metacharacters so an untrusted font name (e.g. from an AI proposal)
 *  can't inject extra declarations when interpolated into a style string. */
function safeFontName(fontName: string): string { return (fontName || '').replace(/[^a-zA-Z0-9 -]/g, '').trim(); }

function hashString(str: string): number { let h = 0; for (let i = 0; i < str.length; i++) { h = (h * 31 + str.charCodeAt(i)) >>> 0; } return h; }
function paletteFromString(str: string): Palette { return PALETTES[hashString(str || 'default') % PALETTES.length]; }
function initialsFrom(name: string): string { return (name || '').trim().split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase() || '').join('') || 'YB'; }
function initialsFromEmail(email: string): string { const local = (email || '').trim().split('@')[0] || ''; const parts = local.split(/[^a-zA-Z0-9]+/).filter(Boolean); const initials = parts.slice(0, 2).map((p) => p[0]?.toUpperCase() || '').join(''); return initials || 'YB'; }
const MIN_PASSWORD_LENGTH = 8;
function domainFromWebsite(url: string): string { if (!url) return ''; return url.trim().toLowerCase().replace(/^https?:\/\//, '').replace(/^www\./, '').split('/')[0]; }
// Resolve a public logo/favicon for a domain. Clearbit's Logo API (logo.clearbit.com)
// was discontinued and its host no longer resolves, so we use Google's favicon service,
// which returns the site's real brand mark at a usable size.
function logoUrlForDomain(domain: string): string { return domain ? `https://www.google.com/s2/favicons?domain=${encodeURIComponent(domain)}&sz=128` : ''; }

function extractColorsFromImage(img: HTMLImageElement): string[] {
  const size = 32;
  const canvas = document.createElement('canvas');
  canvas.width = size; canvas.height = size;
  const ctx = canvas.getContext('2d');
  if (!ctx) return [];
  ctx.drawImage(img, 0, 0, size, size);
  const { data } = ctx.getImageData(0, 0, size, size);
  const counts = new Map<string, number>();
  for (let i = 0; i < data.length; i += 4) {
    const r = data[i], g = data[i + 1], b = data[i + 2], a = data[i + 3];
    if (a < 200) continue;
    const brightness = (r + g + b) / 3;
    if (brightness > 235 || brightness < 20) continue;
    const key = [Math.round(r / 24) * 24, Math.round(g / 24) * 24, Math.round(b / 24) * 24].join(',');
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  const sorted = [...counts.entries()].sort((a, b) => b[1] - a[1]).map(([k]) => k.split(',').map(Number));
  const chosen: number[][] = [];
  for (const [r, g, b] of sorted) {
    if (chosen.some((c) => Math.abs(c[0] - r) < 24 && Math.abs(c[1] - g) < 24 && Math.abs(c[2] - b) < 24)) continue;
    chosen.push([r, g, b]);
    if (chosen.length >= 4) break;
  }
  return chosen.map(([r, g, b]) => '#' + [r, g, b].map((x) => x.toString(16).padStart(2, '0')).join(''));
}

const DISCLOSURE_BY_VERTICAL: Record<VerticalId, string> = {
  insurance: 'Payers must receive a written AutoPay disclosure before enrollment, and premium refunds must be issued within 10 business days of policy cancellation.',
  utilities: 'Payers must be notified of recurring bank-draft terms before enrollment, and late fees are capped at 1.5% per month for residential accounts.',
  tax: 'A separate opt-in, confirmed by email or mail, is required for automatic property-tax withdrawals.',
  other: 'Payers must receive a clear AutoPay disclosure before enrollment, consistent with general consumer protection practice.',
};
const RESTRICTION_BY_STATE: Record<string, string> = {
  Florida: 'AutoPay enrollees must fund payments via bank account only - card enrollment is restricted for recurring payments.',
};

function matchScenario(text: string): ScenarioResult {
  const t = (text || '').toLowerCase();
  if (/(delinquent|delinquency|past due|past-due|overdue|late)/.test(t)) {
    const daysMatch = t.match(/(\d+)\s*(?:day|days)/);
    const days = daysMatch ? Number(daysMatch[1]) : 45;
    const mentionsLateFee = /(late fee|late charge|late-fee|penalty)/.test(t);
    const baseBalance = 342.18;
    const lateFee = 25.00;
    const lines = [`Account is ${days} days past due.`];
    if (mentionsLateFee) {
      lines.push(`A late fee of $${lateFee.toFixed(2)} has been applied.`);
      lines.push(`Outstanding balance: $${(baseBalance + lateFee).toFixed(2)} across 2 unpaid statements (includes late fee).`);
    } else {
      lines.push(`Outstanding balance: $${baseBalance.toFixed(2)} across 2 unpaid statements.`);
    }
    lines.push(days >= 60 ? 'A service suspension warning has been triggered.' : 'A past-due reminder has been sent to the payer.');
    return { title: 'Past-Due Account', intent: 'warning', lines };
  }
  if (/refund/.test(t)) return { title: 'Refund Issued', intent: 'success', lines: ['A refund of $128.42 was issued to the card on file.', 'Funds typically arrive in 3-5 business days.', 'A confirmation email was sent to the payer.'] };
  if (/(dispute|chargeback)/.test(t)) return { title: 'Payment Disputed', intent: 'danger', lines: ['The cardholder has disputed a $98.00 charge.', 'The payment is under review and temporarily held.', 'The biller has 10 days to respond with evidence.'] };
  if (/(large|high balance|big balance)/.test(t)) return { title: 'High Outstanding Balance', intent: 'warning', lines: ['Outstanding balance of $4,820.00 across 3 unpaid statements.', 'Payer may be offered a payment plan.', 'AutoPay enrollment is recommended to prevent recurrence.'] };
  return { title: 'Standard Billing Snapshot', intent: 'info', lines: [`No canned scenario matched "${text.slice(0, 60)}" - showing a standard snapshot instead.`, 'Try keywords like "delinquent", "refund", "dispute", or "large balance".'] };
}

// Validate the payer checkout before reporting a successful (mock) payment. Wallet methods
// redirect to their provider, so only card/bank details are checked here.
function validatePayment(st: State): string | null {
  const isWallet = (['applepay', 'googlepay', 'paypal'] as MethodType[]).includes(st.methodType);
  if (!isWallet && st.methodType === 'card') {
    const digits = st.payCardNumber.replace(/\D/g, '');
    if (digits.length < 13 || digits.length > 19) return 'Enter a valid card number.';
    if (!/^\d{2}\s*\/\s*\d{2}$/.test(st.payCardExpiry.trim())) return 'Enter the card expiry as MM/YY.';
    if (!/^\d{3,4}$/.test(st.payCardCvc.trim())) return 'Enter the 3- or 4-digit security code.';
  } else if (!isWallet && st.methodType === 'bank') {
    if (!/^\d{9}$/.test(st.payBankRouting.replace(/\D/g, ''))) return 'Enter a valid 9-digit routing number.';
    if (st.payBankAccount.replace(/\D/g, '').length < 4) return 'Enter a valid account number.';
  }
  if (st.accountPassword && !st.accountEmail.trim()) return 'Enter an email to create your account, or clear the password to pay as a guest.';
  if (st.accountPassword && st.accountPassword.length < MIN_PASSWORD_LENGTH) return `Password must be at least ${MIN_PASSWORD_LENGTH} characters.`;
  return null;
}

interface AiRecommendations { values: Partial<State>; rationale: Record<string, string>; }
function computeAiRecommendations(vertical: VerticalId | null, states: string[], otherDescription: string): AiRecommendations {
  const isTax = vertical === 'tax';
  const isUtil = vertical === 'utilities';
  const isInsurance = vertical === 'insurance';
  const isOther = vertical === 'other';
  const isFL = (states || []).includes('Florida');
  // The demo create contract currently provisions card and ACH rails. Recommendations must
  // customize presentation without claiming payment capabilities the biller does not have.
  const methods = ['card', 'ach'];
  const fee = isUtil ? 'charge' : 'absorb';
  const otherNote = isOther && otherDescription ? ` Based on your description ("${otherDescription.slice(0, 80)}"), we're starting from standard billing defaults.` : '';
  return {
    values: {
      guestCheckoutAllowed: !isTax,
      offerAutopay: true,
      enrollDuringPayment: true,
      offerPaperless: true,
      reminderChannel: 'both',
      acceptedMethods: methods,
      selfServiceHistory: true,
      selfServiceUpdate: true,
      feeHandling: fee,
    },
    rationale: {
      guestCheckoutAllowed: (isTax
        ? 'Government/tax billers typically require an account for audit purposes - most competitors gate guest checkout here.'
        : isInsurance
          ? 'Policyholders expect to pay a premium quickly using their account or policy number, so guest checkout is enabled to reduce lapse risk.'
          : isUtil
            ? 'Utility payers often pay a one-off bill without logging in, so guest checkout is enabled to reduce drop-off.'
            : 'Guest checkout reduces drop-off - most billers in this vertical let first-time payers pay without an account.') + otherNote,
      offerAutopay: isInsurance
        ? 'AutoPay is a strong fit for recurring premiums - it keeps policies in good standing and cuts lapse-driven churn.'
        : 'AutoPay is standard across competing billers and meaningfully reduces late payments.',
      enrollDuringPayment: isFL ? 'Enrollment during checkout drives adoption, but Florida payers will be limited to bank-funded AutoPay.' : 'Letting payers enroll mid-checkout is the highest-converting placement for AutoPay signup.',
      offerPaperless: 'Paperless adoption is trending up industry-wide and cuts mailing costs.',
      reminderChannel: 'Multi-channel (email + text) reminders outperform single-channel in reducing missed payments.',
      acceptedMethods: isUtil
        ? 'Card and ACH are enabled because they match this biller\'s existing payment rails; additional wallets require an existing rail capability.'
        : isTax
          ? 'Card and ACH match the existing rails and keep government reconciliation straightforward.'
          : isInsurance
            ? 'Card and ACH match the existing rails - ACH is emphasized for recurring premium AutoPay to keep processing costs down.'
            : 'Card and ACH match the biller\'s existing payment rails. The experience does not add or replace money movement.',
      selfServiceHistory: 'Self-service history lookup is table-stakes for competing payment experiences.',
      selfServiceUpdate: 'Letting payers manage their own methods reduces support volume.',
      feeHandling: isUtil
        ? 'Many utility billers pass processing costs through as a convenience fee.'
        : isTax
          ? 'Government billers commonly pass a convenience fee, but this preview absorbs it - switch to pass-through if statute allows.'
          : isInsurance
            ? 'Insurers typically absorb processing fees so premium amounts stay exact and predictable for policyholders.'
            : 'Billers in this vertical commonly absorb fees to keep the payer experience frictionless.',
    },
  };
}

function complianceCategories(vertical: VerticalId | null, state: string): Category[] {
  const verticalLabel = (VERTICALS.find((v) => v.id === vertical) || { label: 'this business' }).label;
  return [
    { label: 'Applicable state regulations', text: `${state} regulates recurring electronic payments under its consumer protection and finance codes; this line of business will follow those rules.` },
    { label: 'PCI considerations', text: 'Card data is tokenized and processed through a PCI DSS Level 1 compliant gateway - no cardholder data is stored on this site.' },
    { label: 'ADA accessibility requirements', text: 'Payment pages must meet WCAG 2.1 AA - this preview uses accessible color contrast, labels, and keyboard navigation.' },
    { label: 'Utility commission requirements', text: vertical === 'utilities' ? `${state}'s public utility commission requires a 3-day right-to-cancel window on recurring bank-draft enrollments.` : `Not applicable - ${verticalLabel} is not regulated by a state utility commission.` },
    { label: 'Required payment disclosures', text: DISCLOSURE_BY_VERTICAL[vertical ?? 'other'] || DISCLOSURE_BY_VERTICAL.utilities },
    { label: 'State-specific payment restrictions', text: RESTRICTION_BY_STATE[state] || `${state} caps late fees at 1.5% per month for this vertical; no method-specific AutoPay restrictions apply.` },
    { label: 'Tax jurisdiction', text: `Convenience fees may be subject to ${state} sales tax depending on payment type - confirm tax rules before publishing.` },
    { label: 'Other jurisdiction-specific requirements', text: `Check county or municipal ordinances layered on top of ${state} law - add them as documents below if applicable.` },
  ];
}

const WIZARD_RESET: Partial<State> = {
  wizardStep: 0, vertical: null, otherVerticalDescription: '', bizName: '', selectedStates: [], stateSearch: '', website: '', skipWebsite: false,
  editingLobId: null, brand: null, compliance: null, agreedToCompliance: false, docs: [], newDocName: '', logoDataUrl: null, logoFetchOk: false, extractedColors: null,
  colorChoice: 'auto', customPrimary: '#085368', customSecondary: '#18b4e9', customAccent: '#dffbfd', fontChoice: 'auto',
  guestCheckoutAllowed: true, offerAutopay: true, enrollDuringPayment: true, offerPaperless: true, reminderChannel: 'email', acceptedMethods: ['card', 'ach'],
  selfServiceHistory: true, selfServiceUpdate: true, feeHandling: 'absorb', aiApplied: false, aiRationale: {}, editingSection: null,
  setupPath: null,
  chatAnswers: ['', '', '', ''], chatStep: 0, chatDraft: '', chatActive: false, analyzingUpload: false, editingChatIndex: null,
  csvFileName: null, importedFields: [], csvOverriddenFields: [], accountNumber: null,
  backendBillerId: null, backendDraft: null, backendSession: null, deployment: null, publishing: false, publishError: null,
  agentActivity: [], activityConnection: 'idle', orchestrationError: null, analysisComplete: false,
  previewProposal: null, previewChatInput: '', previewChatBusy: false, previewChatError: null, previewChatReply: null, previewGenerationMode: null,
};

const INITIAL_STATE: State = {
  screen: 'landing', dashboardSection: 'home', wizardStep: 0, vertical: null, otherVerticalDescription: '', bizName: '', selectedStates: [], stateSearch: '', website: '', skipWebsite: false,
  brand: null, compliance: null,
  payerStep: 0, amount: 128.42, methodType: 'card', autopayOptIn: false, paperlessOptIn: false, processing: false,
  analyzeStage: 0,
  modal: null, purchased: false, accountCreated: false, billerAccountEmail: null, signupEmail: '', signupPassword: '', signupError: null, lobs: [], editingLobId: null, pendingLob: false,
  viewingStatementId: null,
  agreedToCompliance: false, docs: [], newDocName: '', expandedCompliance: [],
  logoDataUrl: null, logoFetchOk: false, extractedColors: null,
  colorChoice: 'auto', customPrimary: '#085368', customSecondary: '#18b4e9', customAccent: '#dffbfd', fontChoice: 'auto',
  guestCheckoutAllowed: true, offerAutopay: true, enrollDuringPayment: true, offerPaperless: true,
  reminderChannel: 'email', acceptedMethods: ['card', 'ach'],
  selfServiceHistory: true, selfServiceUpdate: true, feeHandling: 'absorb',
  setupPath: null,
  chatAnswers: ['', '', '', ''], chatStep: 0, chatDraft: '', chatActive: false, analyzingUpload: false, editingChatIndex: null,
  aiApplied: false, aiRationale: {}, editingSection: null,
  reviewEditingSection: null, reviewSaveError: false,
  csvFileName: null, importedFields: [], csvOverriddenFields: [], accountNumber: null,
  previewScenario: 'payment', complexScenarioText: '', complexScenarioResult: null, scenarioLoading: false,
  previewDevice: 'desktop',
  statementTab: 'current', accountEmail: '', accountPassword: '',
  previewAutopayEnrolled: false, previewAutopayEnrolling: false, previewAutopaySource: 'existing', previewAutopayMethodType: 'card',
  previewPaperlessEnrolled: false,
  payCardNumber: '', payCardExpiry: '', payCardCvc: '', payBankRouting: '', payBankAccount: '', payError: null,
  backendBillerId: null, backendDraft: null, backendSession: null, deployment: null, publishing: false, publishError: null,
  agentActivity: [], activityConnection: 'idle', orchestrationError: null, analysisComplete: false,
  previewChatInput: '', previewChatBusy: false, previewChatError: null, previewProposal: null, previewChatReply: null, previewGenerationMode: null,
};

const TINT = 'var(--invoicecloud-primary-tint)';
const BORDER = 'var(--invoicecloud-surface-default-border)';
const PRIMARY = 'var(--invoicecloud-primary)';
const selBg = (on: boolean) => (on ? TINT : '#fff');
const selBorder = (on: boolean) => (on ? PRIMARY : BORDER);
// Primary wizard call-to-action ("Continue" / "Build My Preview"). When disabled it renders
// with a muted grey fill and not-allowed cursor so it never reads as clickable.
const wizardCtaStyle = (enabled: boolean) =>
  css(
    `background:${enabled ? PRIMARY : 'var(--invoicecloud-utility-neutral-20)'};` +
      `color:${enabled ? '#fff' : 'var(--invoicecloud-utility-neutral-60)'};` +
      `border:none;border-radius:10px;padding:14px 28px;font-size:16px;font-weight:700;` +
      `cursor:${enabled ? 'pointer' : 'not-allowed'}`,
  );

function AgentActivityPanel({
  activity,
  connection,
  complete = false,
}: {
  activity: AgentActivity[];
  connection: State['activityConnection'];
  complete?: boolean;
}) {
  const { invoked, inventory } = partitionAgentActivity(activity);
  const color = (status: AgentActivity['status']) =>
    status === 'completed' ? '#197d00' : status === 'failed' ? '#b42318' : status === 'degraded' ? '#b54708' : status === 'skipped' ? '#667085' : '#0b4f6c';
  const icon = (status: AgentActivity['status']) =>
    status === 'completed' ? '✓' : status === 'failed' || status === 'degraded' ? '!' : status === 'skipped' ? '–' : status === 'discovered' ? '⌕' : '•';
  // Once the run has finished, an agent still reporting a non-terminal status was never delivered
  // an outcome (e.g. the model was unavailable). Settle it to a terminal "skipped" state so no card
  // spins on "running" forever; leave already-terminal statuses untouched.
  const displayStatus = (status: AgentActivity['status']) =>
    complete && !isTerminalStatus(status) ? 'skipped' : status;
  const connectionLabel = complete ? 'Completed' : connection === 'idle' ? 'Waiting' : connection;

  return (
    <section aria-live="polite" style={css('width:100%;max-width:720px;margin-top:20px;padding:16px;border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;background:#fff;box-shadow:var(--invoicecloud-elevation-1)')}>
      <div style={css('display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px')}>
        <span><strong>Research orchestration</strong><small style={css('display:block;margin-top:3px;color:var(--invoicecloud-utility-neutral-70)')}>Invoked agents are tracked separately from the available Foundry inventory.</small></span>
        <small style={css(`color:${connection === 'disconnected' && !complete ? '#b42318' : 'var(--invoicecloud-utility-neutral-70)'}`)}>{connectionLabel}</small>
      </div>
      {invoked.length === 0 ? (
        <p style={css('margin:0;color:var(--invoicecloud-utility-neutral-70);font-size:14px')}>{complete ? 'Orchestration run finished; no eligible agents were engaged.' : 'Waiting for orchestration to discover eligible agents…'}</p>
      ) : (
        <>
        <small style={css('display:block;margin:0 0 8px;color:var(--invoicecloud-utility-neutral-70);font-weight:700')}>Invoked agents</small>
        <div style={css('display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:10px')}>
          {invoked.map(item => {
            const status = displayStatus(item.status);
            const settled = status !== item.status;
            return (
              <article key={item.agent_id} style={css(`border:1px solid ${color(status)}33;border-radius:10px;padding:10px;background:${color(status)}0d`)}>
                <div style={css('display:flex;align-items:center;gap:8px')}><b style={css(`color:${color(status)}`)}>{icon(status)}</b><strong>{item.display_name}</strong></div>
                <code title={item.agent_id} style={css('display:block;margin:5px 0;font-size:11px;overflow:hidden;text-overflow:ellipsis')}>{item.agent_id}</code>
                <small>{settled ? 'Agent did not report a result before the run finished.' : item.summary}</small>
                <small style={css('display:block;margin-top:5px;color:var(--invoicecloud-utility-neutral-70)')}>{agentActivityMeta(item, status, !settled)}</small>
              </article>
            );
          })}
        </div>
        </>
      )}
      {inventory.length > 0 && (
        <details style={css('margin-top:12px;border-top:1px solid var(--invoicecloud-surface-default-border);padding-top:10px')}>
          <summary style={css('cursor:pointer;color:var(--invoicecloud-utility-neutral-80);font-size:13px;font-weight:700')}>Foundry inventory ({inventory.length} not invoked)</summary>
          <div style={css('display:grid;gap:8px;margin-top:8px')}>
            {inventory.map(item => (
              <div key={item.agent_id} style={css('padding:8px 10px;border-radius:8px;background:var(--invoicecloud-utility-neutral-10);font-size:12px')}>
                <strong>{item.display_name}</strong>
                {shouldShowAgentId(item) && <> <code style={css('font-size:11px')}>{item.agent_id}</code></>}
                <span style={css('display:block;margin-top:3px;color:var(--invoicecloud-utility-neutral-70)')}>{item.summary}</span>
              </div>
            ))}
          </div>
        </details>
      )}
    </section>
  );
}

export function App() {
  const [state, setState] = useState<State>(INITIAL_STATE);
  const timers = useRef<ReturnType<typeof setTimeout>[]>([]);
  const saveBtnRef = useRef<HTMLButtonElement>(null);
  const signupDialogRef = useRef<HTMLFormElement>(null);
  const checkoutDialogRef = useRef<HTMLFormElement>(null);
  const previewChatInputRef = useRef<HTMLInputElement>(null);
  const modalTriggerRef = useRef<HTMLElement | null>(null);

  useEffect(() => () => { timers.current.forEach(clearTimeout); }, []);
  useEffect(() => {
    if (!state.modal) return;
    const dialog = state.modal === 'signup' ? signupDialogRef.current : checkoutDialogRef.current;
    if (!dialog) return;
    const focusable = Array.from(dialog.querySelectorAll<HTMLElement>('button, input, select, textarea, [href], [tabindex]:not([tabindex="-1"])'));
    const initial = dialog.querySelector<HTMLElement>('[data-autofocus]') ?? focusable[0];
    initial?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setState(current => ({ ...current, modal: null }));
        return;
      }
      if (event.key !== 'Tab' || focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      modalTriggerRef.current?.focus();
    };
  }, [state.modal]);

  type Updater = Partial<State> | ((st: State) => Partial<State> | null);
  const patch = (u: Updater) => setState((st) => { const next = typeof u === 'function' ? u(st) : u; return next ? { ...st, ...next } : st; });
  const later = (fn: () => void, ms: number) => { timers.current.push(setTimeout(fn, ms)); };

  const s = state;

  const publishableDefinition = (st: State): ExperienceDefinition => {
    if (!st.backendDraft) throw new Error('The generated experience is not ready to publish. Run the agent analysis again.');
    const definition = st.backendDraft.definition;
    const brand = st.brand ?? buildBrand(st);
    return {
      ...definition,
      brand: {
        ...definition.brand,
        display_name: st.bizName,
        primary_color: brand.primary,
        secondary_color: brand.secondary,
        font_family: brand.font,
      },
      pwa: {
        ...definition.pwa,
        name: `${st.bizName} Payments`,
        short_name: st.bizName.slice(0, 24),
        theme_color: brand.primary,
        background_color: brand.accent,
      },
      preferences: {
        guest_checkout_allowed: st.guestCheckoutAllowed,
        offer_autopay: st.offerAutopay,
        enroll_during_payment: st.enrollDuringPayment,
        offer_paperless: st.offerPaperless,
        reminder_channel: st.reminderChannel as 'email' | 'text' | 'both' | 'none',
        accepted_methods: st.acceptedMethods,
        self_service_history: st.selfServiceHistory,
        self_service_updates: st.selfServiceUpdate,
        fee_handling: (st.feeHandling === 'unsure' ? 'undecided' : st.feeHandling) as 'absorb' | 'charge' | 'mixed' | 'undecided',
        preview: {
          default_device: st.previewDevice,
          enabled_scenarios: ['payment', 'history', 'communication', 'complex'],
        },
        recommendation_rationale: st.aiRationale,
      },
    };
  };

  const buildBrand = (st: State): Brand => {
    const { vertical, website, skipWebsite, colorChoice, customPrimary, customSecondary, customAccent, fontChoice, extractedColors } = st;
    let palette: Palette;
    let colorsFromLogo = false;
    if (website && !skipWebsite) {
      if (extractedColors && extractedColors.length >= 2) {
        palette = { primary: extractedColors[0], secondary: extractedColors[1], accent: extractedColors[2] || '#f5f5f7', font: paletteFromString(website).font };
        colorsFromLogo = true;
      } else {
        palette = paletteFromString(website);
      }
    } else if (skipWebsite && colorChoice === 'custom') {
      palette = { primary: customPrimary, secondary: customSecondary, accent: customAccent, font: paletteFromString(vertical || 'default').font };
    } else {
      palette = paletteFromString(vertical || 'default');
    }
    if (skipWebsite && fontChoice !== 'auto') palette = { ...palette, font: fontChoice };
    else if (skipWebsite && fontChoice === 'auto') palette = { ...palette, font: 'Arial' };
    return { ...palette, initials: initialsFrom(st.bizName), colorsFromLogo };
  };

  const recomputeCompliance = (st: State): Compliance => {
    const states = st.selectedStates.length ? st.selectedStates : ['California'];
    const byState = Object.fromEntries(states.map((name) => [name, complianceCategories(st.vertical, name)]));
    return { states, byState };
  };

  const checkLogoFetch = (website: string, skip: boolean) => {
    const domain = domainFromWebsite(website);
    if (!domain || skip) { patch({ logoFetchOk: false, extractedColors: null }); return; }
    const url = logoUrlForDomain(domain);
    // Whether we can show the logo only depends on the image rendering, so load it without CORS.
    const display = new Image();
    display.onload = () => patch({ logoFetchOk: true });
    display.onerror = () => patch({ logoFetchOk: false });
    display.src = url;
    // Color sampling reads pixels off a canvas, which requires a CORS-clean image. When the host
    // doesn't allow it the load fails and we keep the simulated palette rather than blocking the logo.
    const sample = new Image();
    sample.crossOrigin = 'anonymous';
    sample.onload = () => {
      try {
        const colors = extractColorsFromImage(sample);
        patch({ extractedColors: colors.length ? colors : null });
      } catch { patch({ extractedColors: null }); }
    };
    sample.onerror = () => patch({ extractedColors: null });
    sample.src = url;
  };

  // ---- handlers ----
  const goTry = () => { trackEvent('studio.onboarding_started'); patch({ screen: 'wizard', ...WIZARD_RESET }); };
  const setColorChoice = (v: 'auto' | 'custom') => patch({ colorChoice: v });
  const setCustomPrimary = (e: React.ChangeEvent<HTMLInputElement>) => patch({ customPrimary: e.target.value });
  const setCustomSecondary = (e: React.ChangeEvent<HTMLInputElement>) => patch({ customSecondary: e.target.value });
  const setCustomAccent = (e: React.ChangeEvent<HTMLInputElement>) => patch({ customAccent: e.target.value });
  const setFontChoice = (e: string | React.ChangeEvent<HTMLSelectElement>) => patch({ fontChoice: typeof e === 'string' ? e : e.target.value });
  const selectVertical = (v: VerticalId) => patch({ vertical: v });
  const setOtherVerticalDescription = (e: React.ChangeEvent<HTMLInputElement>) => patch({ otherVerticalDescription: e.target.value });
  const setBizName = (e: React.ChangeEvent<HTMLInputElement>) => patch({ bizName: e.target.value });
  const toggleState = (name: string) => patch((st) => ({ selectedStates: st.selectedStates.includes(name) ? st.selectedStates.filter((x) => x !== name) : [...st.selectedStates, name] }));
  const setStateSearch = (e: React.ChangeEvent<HTMLInputElement>) => patch({ stateSearch: e.target.value });
  const toggleAcceptedMethod = (id: string) => patch((st) => ({ acceptedMethods: st.acceptedMethods.includes(id) ? st.acceptedMethods.filter((x) => x !== id) : [...st.acceptedMethods, id] }));

  const setWebsite = (e: React.ChangeEvent<HTMLInputElement>) => { const website = e.target.value; const skip = !website.trim(); patch({ website, skipWebsite: skip, logoFetchOk: false, extractedColors: null }); checkLogoFetch(website, skip); };
  const toggleSkipWebsite = () => { const skip = !s.skipWebsite; const website = skip ? '' : s.website; patch({ skipWebsite: skip, website, logoFetchOk: false }); checkLogoFetch(website, skip); };
  const setLogoFile = (e: React.ChangeEvent<HTMLInputElement>) => { const file = e.target.files && e.target.files[0]; if (!file) return; const reader = new FileReader(); reader.onload = () => patch({ logoDataUrl: reader.result as string }); reader.readAsDataURL(file); };
  const clearLogo = () => patch({ logoDataUrl: null });

  const setSetupPathUpload = () => patch({ setupPath: 'upload', chatActive: false, analyzingUpload: false });
  const setSetupPathManual = () => patch({ setupPath: 'manual', chatActive: true });
  const setChatDraft = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => patch({ chatDraft: e.target.value });
  const applyChatSuggestion = (text: string) => patch({ chatDraft: text });
  const submitChatAnswer = () => patch((st) => { if (!st.chatDraft.trim()) return null; const chatAnswers = [...st.chatAnswers]; chatAnswers[st.chatStep] = st.chatDraft.trim(); return { chatAnswers, chatDraft: '', chatStep: Math.min(4, st.chatStep + 1) }; });
  const editChatAnswerClick = (i: number) => patch((st) => ({ editingChatIndex: i, chatDraft: st.chatAnswers[i] }));
  const saveChatAnswerEdit = () => patch((st) => { if (st.editingChatIndex === null || !st.chatDraft.trim()) return null; const chatAnswers = [...st.chatAnswers]; chatAnswers[st.editingChatIndex] = st.chatDraft.trim(); return { chatAnswers, chatDraft: '', editingChatIndex: null }; });
  const editBrandLogoClick = () => patch((st) => ({ editingSection: st.editingSection === 'brandlogo' ? null : 'brandlogo' }));
  const editBrandColorsClick = () => patch((st) => ({ editingSection: st.editingSection === 'brandcolors' ? null : 'brandcolors' }));
  const editBrandFontClick = () => patch((st) => ({ editingSection: st.editingSection === 'brandfont' ? null : 'brandfont' }));
  const colorChoiceAutoClick = () => setColorChoice('auto');
  const colorChoiceCustomClick = () => setColorChoice('custom');
  const fontChoiceAutoClick = () => setFontChoice('auto');
  const fontChoiceCustomClick = () => setFontChoice(GOOGLE_FONTS[0]);

  const nextStep = () => {
    const completedStep = CHECKLIST_STEPS[s.wizardStep];
    if (completedStep) trackEvent('studio.checklist_step_completed', { step: completedStep, biller_id: s.backendBillerId ?? undefined });
    patch((st) => ({ wizardStep: Math.min(2, st.wizardStep + 1) }));
  };
  const prevStep = () => patch((st) => ({ wizardStep: Math.max(0, st.wizardStep - 1) }));

  const onCsvFile = (e: React.ChangeEvent<HTMLInputElement>) => { const file = e.target.files && e.target.files[0]; if (!file) return; const reader = new FileReader(); reader.onload = () => { applyCsv(reader.result as string, file.name); patch({ analyzingUpload: true }); later(() => patch({ analyzingUpload: false, chatActive: true }), 1600); }; reader.readAsText(file); };
  const applyCsv = (text: string, fileName: string) => {
    const lines = text.split(/\r?\n/).filter((l) => l.trim().length);
    if (lines.length < 2) return;
    const headers = lines[0].split(',').map((h) => h.trim());
    const row = lines[1].split(',').map((v) => v.trim());
    const record: Record<string, string> = {};
    headers.forEach((h, i) => { record[h.toLowerCase().replace(/[^a-z]/g, '')] = row[i]; });
    const parseBool = (v: string): boolean | undefined => { if (v == null) return undefined; const t = v.toLowerCase(); if (['yes', 'true', 'y', '1'].includes(t)) return true; if (['no', 'false', 'n', '0'].includes(t)) return false; return undefined; };
    const updates: Record<string, unknown> = {};
    const overridden: string[] = [];
    const display: ImportedField[] = [];
    const maybe = (key: string, field: string, transform: ((v: string) => unknown) | null, lbl: string) => {
      if (record[key] === undefined || record[key] === '') return;
      const val = transform ? transform(record[key]) : record[key];
      if (val === undefined) return;
      updates[field] = val; overridden.push(field);
      display.push({ label: lbl, value: Array.isArray(val) ? val.join(', ') : String(val) });
    };
    const oneOf = (opts: string[]) => (v: string) => (opts.includes(v.toLowerCase()) ? v.toLowerCase() : undefined);
    const list = (v: string) => v.split(/[;|]/).map((x) => x.trim().toLowerCase()).filter(Boolean);
    maybe('accountnumber', 'accountNumber', (v) => v, 'Account number');
    maybe('guestcheckout', 'guestCheckoutAllowed', parseBool, 'Guest checkout');
    maybe('autopay', 'offerAutopay', parseBool, 'AutoPay offered');
    maybe('offerautopay', 'offerAutopay', parseBool, 'AutoPay offered');
    maybe('enrollduringpayment', 'enrollDuringPayment', parseBool, 'Enroll during payment');
    maybe('paperless', 'offerPaperless', parseBool, 'Paperless offered');
    maybe('offerpaperless', 'offerPaperless', parseBool, 'Paperless offered');
    maybe('reminderchannel', 'reminderChannel', oneOf(['email', 'text', 'both', 'none']), 'Reminders');
    maybe('reminders', 'reminderChannel', oneOf(['email', 'text', 'both', 'none']), 'Reminders');
    maybe('acceptedmethods', 'acceptedMethods', list, 'Accepted methods');
    maybe('paymentmethods', 'acceptedMethods', list, 'Accepted methods');
    maybe('selfservicehistory', 'selfServiceHistory', parseBool, 'Self-service history');
    maybe('selfserviceupdate', 'selfServiceUpdate', parseBool, 'Self-service updates');
    maybe('feehandling', 'feeHandling', oneOf(['absorb', 'charge', 'mixed', 'unsure']), 'Fee handling');
    patch((st) => ({ ...(updates as Partial<State>), csvFileName: fileName, importedFields: display, csvOverriddenFields: [...new Set([...(st.csvOverriddenFields || []), ...overridden])] }));
  };

  const runAnalysis = async () => {
    trackEvent('studio.checklist_step_completed', { step: CHECKLIST_STEPS[2], biller_id: s.backendBillerId ?? undefined });
    let aiUpdates: Partial<State> = {};
    if (!s.aiApplied) {
      const rec = computeAiRecommendations(s.vertical, s.selectedStates, s.otherVerticalDescription);
      const values: Partial<State> = { ...rec.values };
      const rationale: Record<string, string> = { ...rec.rationale };
      (s.csvOverriddenFields || []).forEach((f) => { delete (values as Record<string, unknown>)[f]; rationale[f] = 'Imported from your data file.'; });
      aiUpdates = { ...values, aiApplied: true, aiRationale: rationale };
    }
    patch({ screen: 'analyzing', analyzeStage: 0, orchestrationError: null, agentActivity: [], analysisComplete: false, ...aiUpdates });
    later(() => patch({ analyzeStage: 1 }), 700);
    later(() => patch({ analyzeStage: 2 }), 1400);
    let events: EventSource | undefined;
    try {
      let billerId = s.backendBillerId;
      if (!billerId) {
        const vertical = VERTICALS.find((item) => item.id === s.vertical)?.label ?? 'Other';
        const slug = toBillerSlug(s.bizName);
        const website = s.website.trim()
          ? (/^https?:\/\//i.test(s.website.trim()) ? s.website.trim() : `https://${s.website.trim()}`)
          : undefined;
        const primaryState = s.selectedStates[0] ?? 'California';
        const created = await api.create({
          display_name: s.bizName,
          slug,
          bill_type: vertical,
          postal_code: STATE_POSTAL_CODE[primaryState] ?? '10001',
          website,
        });
        billerId = created.biller.biller_id;
        patch({ backendBillerId: billerId, backendDraft: created.draft, backendSession: created.session });
      }

      events = new EventSource(activityUrl(billerId));
      patch({ activityConnection: 'connecting' });
      events.onopen = () => patch({ activityConnection: 'connected' });
      events.addEventListener('agent_activity', raw => {
        try {
          const item = JSON.parse((raw as MessageEvent).data) as AgentActivity;
          if (!item.event_id || !item.agent_id || !item.status) throw new Error('Agent activity payload is incomplete.');
          patch(st => ({
            agentActivity: [...st.agentActivity.filter(existing => existing.event_id !== item.event_id), item]
              .sort((left, right) => left.sequence - right.sequence),
          }));
          if (item.status === 'failed' || item.status === 'degraded') {
            logError('studio.agent.unhealthy', new Error(item.summary), {
              biller_id: billerId, agent_id: item.agent_id, trace_id: item.trace_id, error_code: item.error_code,
            });
          }
        } catch (caught) {
          logError('studio.activity.invalid_event', caught, { biller_id: billerId });
        }
      });
      events.onerror = () => patch({ activityConnection: 'disconnected' });

      trackEvent('studio.chat_message_sent', { biller_id: billerId }); // count only; message text never leaves the page
      const selectedCategories = s.chatAnswers[0]?.trim();
      const billingAnswers = selectedCategories
        ? [{ dimension: 'categories' as const, answer: selectedCategories }]
        : undefined;
      const chat = await api.chat(
        billerId,
        `Build a ${s.vertical ?? 'custom'} payment experience for ${s.bizName}. ` +
        `Serve ${s.selectedStates.join(', ') || 'the selected market'}; methods: ${s.acceptedMethods.join(', ')}. ` +
        `Use the supplied website and preserve the existing payment rails.`,
        billingAnswers,
      );
      let finalActivity: AgentActivity[] | undefined;
      try {
        finalActivity = (await api.activity(billerId)).activity;
      } catch (caught) {
        logError('studio.activity.final_snapshot_failed', caught, { biller_id: billerId });
      }
      logEvent('studio.orchestration.completed', { biller_id: billerId, revision: chat.draft.revision });
      patch(st => ({
        compliance: recomputeCompliance(st),
        brand: buildBrand(st),
        analyzeStage: 2,
        backendDraft: chat.draft,
        backendSession: chat.session,
        previewChatReply: chat.reply,
        acceptedMethods: chat.draft.definition.preferences?.accepted_methods ?? st.acceptedMethods,
        agentActivity: finalActivity ?? st.agentActivity,
        analysisComplete: true,
        activityConnection: 'idle',
      }));
      trackEvent('studio.draft_generated', { biller_id: billerId });
      trackEvent('studio.validation_result', { outcome: validationOutcome(chat.draft), biller_id: billerId });
    } catch (caught) {
      const message = errorMessage(caught);
      logError('studio.orchestration.failed', caught, { biller_id: s.backendBillerId });
      patch({ orchestrationError: message, activityConnection: 'disconnected' });
    } finally {
      events?.close();
    }
  };

  const openReviewSection = (name: string) => patch({ reviewEditingSection: name });
  const saveBrandSection = () => patch((st) => ({ reviewEditingSection: null, brand: buildBrand(st) }));
  const saveVerticalSection = () => patch((st) => ({ reviewEditingSection: null, compliance: recomputeCompliance(st) }));
  const saveLocationSection = () => patch((st) => ({ reviewEditingSection: null, compliance: recomputeCompliance(st) }));
  const closeSaveErrorModal = () => { patch({ reviewSaveError: false }); setTimeout(() => { saveBtnRef.current?.focus(); }, 0); };

  const confirmPreview = () => { if (s.reviewEditingSection) { patch({ reviewSaveError: true }); return; } trackEvent('studio.preview_opened', { device: s.previewDevice, biller_id: s.backendBillerId ?? undefined }); patch({ screen: 'preview', payerStep: 0, methodType: 'card', autopayOptIn: false, paperlessOptIn: false, processing: false, statementTab: 'current', accountEmail: '', accountPassword: '', payCardNumber: '', payCardExpiry: '', payCardCvc: '', payBankRouting: '', payBankAccount: '', payError: null, previewAutopayEnrolled: false, previewAutopayEnrolling: false, previewAutopaySource: 'existing', previewAutopayMethodType: 'card', previewPaperlessEnrolled: false }); };
  const redoWizard = () => patch({ screen: 'wizard', wizardStep: 0 });
  const reviewCompletedResearch = () => patch({ screen: 'analyzing' });
  const backToResults = () => patch({ screen: 'results', payerStep: 0, processing: false });
  const submitPreviewChange = async (event: FormEvent) => {
    event.preventDefault();
    if (!s.backendBillerId || !s.previewChatInput.trim() || s.previewChatBusy) return;
    patch({ previewChatBusy: true, previewChatError: null, previewProposal: null });
    const events = new EventSource(activityUrl(s.backendBillerId));
    patch({ activityConnection: 'connecting' });
    events.onopen = () => patch({ activityConnection: 'connected' });
    events.addEventListener('agent_activity', raw => {
      try {
        const item = JSON.parse((raw as MessageEvent).data) as AgentActivity;
        patch(st => ({ agentActivity: [...st.agentActivity.filter(existing => existing.event_id !== item.event_id), item].sort((left, right) => left.sequence - right.sequence) }));
      } catch (caught) { logError('studio.preview_chat.invalid_event', caught, { biller_id: s.backendBillerId }); }
    });
    events.onerror = () => patch({ activityConnection: 'disconnected' });
    try {
      const discoveryActive = !!s.backendSession?.current_question;
      const message = discoveryActive
        ? s.previewChatInput.trim()
        : `Modify the existing payer experience preview only as requested. Preserve existing payment rails. Request: ${s.previewChatInput.trim()}`;
      const response = await api.chat(s.backendBillerId, message);
      let finalActivity: AgentActivity[] | undefined;
      try {
        finalActivity = (await api.activity(s.backendBillerId)).activity;
      } catch (caught) {
        logError('studio.preview_chat.activity_snapshot_failed', caught, { biller_id: s.backendBillerId });
      }
      patch({
        previewChatBusy: false,
        backendSession: response.session,
        backendDraft: discoveryActive ? response.draft : s.backendDraft,
        previewProposal: discoveryActive ? null : response.draft,
        previewChatInput: discoveryActive ? '' : s.previewChatInput,
        previewChatReply: response.reply,
        previewGenerationMode: response.generation_mode ?? null,
        ...(finalActivity ? { agentActivity: finalActivity } : {}),
      });
    } catch (caught) {
      logError('studio.preview_chat.failed', caught, { biller_id: s.backendBillerId });
      patch({ previewChatBusy: false, previewChatError: errorMessage(caught) });
    } finally { window.setTimeout(() => { events.close(); patch({ activityConnection: 'idle' }); }, 500); }
  };
  const reopenBillingQuestion = async (questionId: string) => {
    if (!s.backendBillerId || s.previewChatBusy) return;
    patch({ previewChatBusy: true, previewChatError: null, previewProposal: null });
    try {
      const session = await api.reopenBillingQuestion(s.backendBillerId, questionId);
      patch({ backendSession: session, previewChatBusy: false, previewChatReply: session.current_question?.prompt ?? 'Answer reopened.', previewChatInput: '' });
    } catch (caught) {
      logError('studio.billing_discovery.reopen_failed', caught, { biller_id: s.backendBillerId, question_id: questionId });
      patch({ previewChatBusy: false, previewChatError: errorMessage(caught) });
    }
  };
  const acceptPreviewChange = () => patch(st => {
    const proposal = st.previewProposal;
    if (!proposal) return null;
    const preferences = proposal.definition.preferences;
    return {
      backendDraft: proposal,
      brand: {
        primary: proposal.definition.brand.primary_color,
        secondary: proposal.definition.brand.secondary_color,
        accent: proposal.definition.pwa.background_color,
        font: proposal.definition.brand.font_family ?? st.brand?.font ?? 'Inter',
        initials: initialsFrom(proposal.definition.brand.display_name),
      },
      acceptedMethods: preferences?.accepted_methods ?? st.acceptedMethods,
      guestCheckoutAllowed: preferences?.guest_checkout_allowed ?? st.guestCheckoutAllowed,
      offerAutopay: preferences?.offer_autopay ?? st.offerAutopay,
      offerPaperless: preferences?.offer_paperless ?? st.offerPaperless,
      previewProposal: null,
      previewChatInput: '',
      previewChatReply: 'Changes accepted and applied to this preview.',
    };
  });
  const rejectPreviewChange = async () => {
    if (!s.backendBillerId || !s.backendDraft || !s.previewProposal) return;
    patch({ previewChatBusy: true, previewChatError: null });
    try {
      const restored = await api.update(s.backendBillerId, s.backendDraft.definition, s.previewProposal.e_tag);
      patch({ backendDraft: restored, previewProposal: null, previewChatBusy: false, previewChatReply: 'Proposed changes discarded.', previewChatInput: '', previewGenerationMode: null });
    } catch (caught) {
      logError('studio.preview_chat.reject_failed', caught, { biller_id: s.backendBillerId });
      patch({ previewChatBusy: false, previewChatError: errorMessage(caught) });
    }
  };
  const setNewDocName = (e: React.ChangeEvent<HTMLInputElement>) => patch({ newDocName: e.target.value });
  const addDocument = () => patch((st) => (st.newDocName.trim() ? { docs: [...st.docs, st.newDocName.trim()], newDocName: '' } : null));
  const removeDocument = (i: number) => patch((st) => ({ docs: st.docs.filter((_, idx) => idx !== i) }));
  const toggleComplianceState = (name: string) => patch((st) => ({ expandedCompliance: st.expandedCompliance.includes(name) ? st.expandedCompliance.filter((x) => x !== name) : [...st.expandedCompliance, name] }));

  const payerGoPay = () => patch({ payerStep: 1 });
  const payerBack = () => patch((st) => ({ payerStep: Math.max(0, st.payerStep - 1) }));
  const selectMethodType = (t: MethodType) => patch({ methodType: t, payError: null });
  const toggleAutopay = () => patch((st) => { const turningOn = !st.autopayOptIn; const isFL = !!st.compliance?.states.includes('Florida'); return { autopayOptIn: turningOn, methodType: turningOn && isFL ? 'bank' : st.methodType, paperlessOptIn: turningOn && st.offerPaperless ? true : st.paperlessOptIn, payError: null }; });
  const togglePaperless = () => patch((st) => ({ paperlessOptIn: !st.paperlessOptIn }));
  const setPayCardNumber = (e: React.ChangeEvent<HTMLInputElement>) => patch({ payCardNumber: e.target.value, payError: null });
  const setPayCardExpiry = (e: React.ChangeEvent<HTMLInputElement>) => patch({ payCardExpiry: e.target.value, payError: null });
  const setPayCardCvc = (e: React.ChangeEvent<HTMLInputElement>) => patch({ payCardCvc: e.target.value, payError: null });
  const setPayBankRouting = (e: React.ChangeEvent<HTMLInputElement>) => patch({ payBankRouting: e.target.value, payError: null });
  const setPayBankAccount = (e: React.ChangeEvent<HTMLInputElement>) => patch({ payBankAccount: e.target.value, payError: null });
  const submitPayment = () => {
    const err = validatePayment(s);
    if (err) { patch({ payError: err }); return; }
    patch({ processing: true, payError: null });
    later(() => patch({ processing: false, payerStep: 2 }), 900);
  };
  const payerRestart = () => patch({ payerStep: 0, methodType: 'card', autopayOptIn: false, paperlessOptIn: false, processing: false, viewingStatementId: null, statementTab: 'current', accountEmail: '', accountPassword: '', payCardNumber: '', payCardExpiry: '', payCardCvc: '', payBankRouting: '', payBankAccount: '', payError: null, previewAutopayEnrolled: false, previewAutopayEnrolling: false, previewAutopaySource: 'existing', previewAutopayMethodType: 'card', previewPaperlessEnrolled: false });
  const selectScenario = (name: string) => patch({ previewScenario: name, payerStep: 0 });
  const setStatementTab = (tab: 'current' | 'past') => patch({ statementTab: tab });
  const setAccountEmail = (e: React.ChangeEvent<HTMLInputElement>) => patch({ accountEmail: e.target.value });
  const setAccountPassword = (e: React.ChangeEvent<HTMLInputElement>) => patch({ accountPassword: e.target.value });
  const togglePreviewAutopayEnrolling = () => patch((st) => ({ previewAutopayEnrolling: !st.previewAutopayEnrolling }));
  const choosePreviewAutopayExisting = () => patch({ previewAutopaySource: 'existing' });
  const choosePreviewAutopayNew = () => patch({ previewAutopaySource: 'new' });
  const previewAutopaySetCard = () => patch({ previewAutopayMethodType: 'card' });
  const previewAutopaySetBank = () => patch({ previewAutopayMethodType: 'bank' });
  const confirmPreviewAutopayEnroll = () => patch({ previewAutopayEnrolled: true, previewAutopayEnrolling: false });
  const unenrollPreviewAutopay = () => patch({ previewAutopayEnrolled: false });
  const togglePreviewPaperless = () => patch((st) => ({ previewPaperlessEnrolled: !st.previewPaperlessEnrolled }));
  const setPreviewDevice = (d: 'desktop' | 'mobile') => patch({ previewDevice: d });
  const setComplexScenarioText = (e: React.ChangeEvent<HTMLTextAreaElement>) => patch({ complexScenarioText: e.target.value });
  const previewScenarioClick = () => { patch({ scenarioLoading: true }); const text = s.complexScenarioText; later(() => patch({ scenarioLoading: false, complexScenarioResult: matchScenario(text) }), 700); };
  const viewStatement = (id: string) => patch({ viewingStatementId: id });
  const closeStatement = () => patch({ viewingStatementId: null });
  const payFromStatement = () => patch({ viewingStatementId: null, payerStep: 1 });

  const rememberModalTrigger = () => {
    modalTriggerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  };
  const openSignup = () => { rememberModalTrigger(); patch({ modal: 'signup', pendingLob: true, signupError: null }); };
  const continueBillingInterview = () => {
    patch({
      screen: 'preview',
      modal: null,
      pendingLob: true,
      publishError: null,
      previewChatError: null,
      previewChatReply: billingInterviewPrompt(s.backendSession),
    });
    window.setTimeout(() => previewChatInputRef.current?.focus(), 0);
  };
  const openCheckout = () => {
    if (billingInterviewPending(s.backendSession)) { continueBillingInterview(); return; }
    rememberModalTrigger();
    trackEvent('studio.purchase_started', { biller_id: s.backendBillerId ?? undefined });
    patch({ modal: 'checkout' });
  };
  const publishFromPreview = () => {
    if (billingInterviewPending(s.backendSession)) { continueBillingInterview(); return; }
    rememberModalTrigger();
    trackEvent('studio.purchase_started', { biller_id: s.backendBillerId ?? undefined });
    patch({ pendingLob: true, modal: 'checkout' });
  };
  const closeModal = () => patch({ modal: null });
  const setSignupEmail = (e: React.ChangeEvent<HTMLInputElement>) => patch({ signupEmail: e.target.value, signupError: null });
  const setSignupPassword = (e: React.ChangeEvent<HTMLInputElement>) => patch({ signupPassword: e.target.value, signupError: null });

  const saveLob = (published: boolean) => patch((st) => {
    const lob: Lob = {
      id: st.editingLobId || 'lob_' + Date.now(),
      vertical: st.vertical, bizName: st.bizName, selectedStates: st.selectedStates, website: st.website, skipWebsite: st.skipWebsite,
      brand: st.brand, compliance: st.compliance, published, docs: st.docs, logoDataUrl: st.logoDataUrl, accountNumber: st.accountNumber,
      guestCheckoutAllowed: st.guestCheckoutAllowed, offerAutopay: st.offerAutopay, enrollDuringPayment: st.enrollDuringPayment, offerPaperless: st.offerPaperless,
      reminderChannel: st.reminderChannel, acceptedMethods: st.acceptedMethods, selfServiceHistory: st.selfServiceHistory, selfServiceUpdate: st.selfServiceUpdate, feeHandling: st.feeHandling,
      backendBillerId: st.backendBillerId,
      backendDraft: st.backendDraft,
      backendSession: st.backendSession,
      deployment: st.deployment,
    };
    const exists = st.lobs.some((l) => l.id === lob.id);
    const lobs = exists ? st.lobs.map((l) => (l.id === lob.id ? lob : l)) : [...st.lobs, lob];
    return { lobs, accountCreated: true, purchased: st.purchased || published, screen: 'dashboard', dashboardSection: 'home', modal: null, editingLobId: null, pendingLob: false };
  });
  const submitSignup = (e: FormEvent) => {
    e.preventDefault();
    const email = s.signupEmail.trim();
    if (!email) { patch({ signupError: 'Enter your work email.' }); return; }
    if (!s.signupPassword) { patch({ signupError: 'Enter a password.' }); return; }
    if (s.signupPassword.length < MIN_PASSWORD_LENGTH) { patch({ signupError: `Password must be at least ${MIN_PASSWORD_LENGTH} characters.` }); return; }
    patch({ billerAccountEmail: email, accountCreated: true, signupError: null, signupPassword: '' });
    if (s.pendingLob) saveLob(false); else patch({ modal: null });
  };
  const submitCheckout = async (e: FormEvent) => {
    e.preventDefault();
    if (s.publishing) return;
    if (billingInterviewPending(s.backendSession)) {
      logEvent('studio.publish_interview_incomplete', { biller_id: s.backendBillerId });
      continueBillingInterview();
      return;
    }
    patch({ publishing: true, publishError: null });
    trackEvent('studio.publish_requested', { biller_id: s.backendBillerId ?? undefined });
    try {
      if (!s.backendBillerId) throw new Error('This experience is not connected to a biller. Run the agent analysis before publishing.');

      let deployment = s.deployment;
      const currentState = deployment?.state.toLowerCase();
      if (!deployment || currentState === 'failed' || currentState === 'rolled_back' || currentState === 'ready') {
        if (currentState === 'failed' || currentState === 'rolled_back') {
          throw new Error(PUBLISH_FAILURE_MESSAGE);
        }
        const updated = await api.update(s.backendBillerId, publishableDefinition(s), s.backendDraft?.e_tag);
        patch({ backendDraft: updated });
        const blockingFindings = (updated.findings ?? []).filter(finding => finding.severity === 2 || String(finding.severity).toLowerCase() === 'blocking');
        if (blockingFindings.length > 0) {
          throw new UiRequestError(
            'This experience is not ready to publish. Resolve the items below and try again.',
            422,
            'experience_validation_blocked',
            undefined,
            false,
            blockingFindings,
          );
        }
        const approved = await api.approve(s.backendBillerId, updated.revision);
        deployment = await api.publish(s.backendBillerId, approved.revision);
        patch({ backendDraft: approved, deployment });
      }

      for (let attempt = 0; attempt < 45 && deployment.state.toLowerCase() !== 'ready'; attempt += 1) {
        const state = deployment.state.toLowerCase();
        if (state === 'failed' || state === 'rolled_back') {
          throw new Error(PUBLISH_FAILURE_MESSAGE);
        }
        await new Promise(resolve => window.setTimeout(resolve, 1000));
        deployment = await api.deployment(s.backendBillerId, deployment.deployment_id);
        patch({ deployment });
      }
      if (deployment.state.toLowerCase() !== 'ready') {
        throw new Error('Publication is still processing. You can safely retry status checking.');
      }

      if (s.pendingLob) saveLob(true);
      else patch(st => ({
        purchased: true,
        modal: null,
        lobs: st.lobs.map(lob => lob.id === st.editingLobId || (!st.editingLobId && lob.bizName === st.bizName) ? { ...lob, published: true } : lob),
      }));
      trackEvent('studio.publish_completed', { biller_id: s.backendBillerId ?? undefined });
      trackEvent('studio.purchase_completed', { biller_id: s.backendBillerId ?? undefined });
      patch({ deployment, publishing: false, publishError: null });
    } catch (caught) {
      const message = caught instanceof UiRequestError ? caught.message : errorMessage(caught);
      trackEvent('studio.publish_failed', { error_category: categorizeError(caught), biller_id: s.backendBillerId ?? undefined });
      logError('studio.publish.failed', caught, {
        biller_id: s.backendBillerId,
        revision: s.backendDraft?.revision,
        deployment_id: s.deployment?.deployment_id,
        finding_codes: caught instanceof UiRequestError ? caught.findings.map(finding => finding.code) : [],
      });
      patch({
        publishing: false,
        publishError: {
          message,
          findings: caught instanceof UiRequestError ? caught.findings : [],
          reference: caught instanceof UiRequestError ? caught.correlationId : undefined,
        },
      });
    }
  };

  const addLob = () => { trackEvent('studio.onboarding_started'); patch({ screen: 'wizard', ...WIZARD_RESET }); };
  const editLob = (lob: Lob) => {
    patch({
      screen: 'wizard', wizardStep: 0, vertical: lob.vertical, bizName: lob.bizName, selectedStates: lob.selectedStates || [], website: lob.website, skipWebsite: !!lob.skipWebsite,
      brand: lob.brand, compliance: lob.compliance, editingLobId: lob.id, agreedToCompliance: true, docs: lob.docs || [], newDocName: '', logoDataUrl: lob.logoDataUrl || null, logoFetchOk: false, extractedColors: null,
      guestCheckoutAllowed: lob.guestCheckoutAllowed, offerAutopay: lob.offerAutopay, enrollDuringPayment: lob.enrollDuringPayment, offerPaperless: lob.offerPaperless,
      reminderChannel: lob.reminderChannel, acceptedMethods: lob.acceptedMethods || ['card', 'ach'], selfServiceHistory: lob.selfServiceHistory, selfServiceUpdate: lob.selfServiceUpdate, feeHandling: lob.feeHandling, accountNumber: lob.accountNumber || null,
      backendBillerId: lob.backendBillerId, backendDraft: lob.backendDraft, backendSession: lob.backendSession, deployment: lob.deployment, publishing: false, publishError: null,
      aiApplied: true, aiRationale: {}, editingSection: null,
    });
    checkLogoFetch(lob.website, !!lob.skipWebsite);
  };
  const previewLob = (lob: Lob) => {
    trackEvent('studio.preview_opened', { device: s.previewDevice, biller_id: lob.backendBillerId ?? undefined });
    patch({
      screen: 'preview', payerStep: 0, brand: lob.brand, compliance: lob.compliance, bizName: lob.bizName, vertical: lob.vertical, website: lob.website, skipWebsite: !!lob.skipWebsite, logoDataUrl: lob.logoDataUrl || null, logoFetchOk: false,
      guestCheckoutAllowed: lob.guestCheckoutAllowed, offerAutopay: lob.offerAutopay, enrollDuringPayment: lob.enrollDuringPayment, offerPaperless: lob.offerPaperless, acceptedMethods: lob.acceptedMethods || ['card', 'ach'], accountNumber: lob.accountNumber || null,
      editingLobId: lob.id, backendBillerId: lob.backendBillerId, backendDraft: lob.backendDraft, backendSession: lob.backendSession, deployment: lob.deployment, publishing: false, publishError: null,
      methodType: 'card', autopayOptIn: false, paperlessOptIn: false,
    });
    checkLogoFetch(lob.website, !!lob.skipWebsite);
  };

  // ---- derived values ----
  const brand: Brand = s.brand || { primary: '#085368', secondary: '#18b4e9', accent: '#dffbfd', font: 'Arial', initials: initialsFrom(s.bizName) };
  useEffect(() => { ensureFontLoaded(brand.font); }, [brand.font]);
  const compliance: Compliance = s.compliance || { states: [], byState: {} };
  const docLabels = VERTICAL_DOC_LABELS[s.vertical ?? 'insurance'];
  const lineLabels = LINEITEM_LABELS[s.vertical ?? 'insurance'];

  const verticals = VERTICALS.map((v) => ({ ...v, iconSrc: asset(`assets/icons/${v.icon}.svg`), selected: v.id === s.vertical, onSelect: () => selectVertical(v.id) }));
  const manualQuestionsComplete = s.setupPath !== 'manual' || !!s.chatAnswers[0]?.trim();
  const stepValid = [
    !!s.vertical && (s.vertical !== 'other' || s.otherVerticalDescription.trim().length > 0),
    s.bizName.trim().length > 1 && s.selectedStates.length > 0 && !!s.setupPath && manualQuestionsComplete,
    true,
  ];
  const wizardCanProceed = stepValid[s.wizardStep];
  const isLastWizardStep = s.wizardStep === 2;

  const stateOptions = STATE_OPTIONS.filter((name) => name.toLowerCase().includes(s.stateSearch.trim().toLowerCase())).map((name) => ({ name, selected: s.selectedStates.includes(name), onToggle: () => toggleState(name) }));
  const stepNames = ['Vertical', 'Business Details', 'Brand Details', 'Review'];
  const stepLabels = stepNames.map((text, i) => ({ text, weight: i === s.wizardStep ? 700 : 400, color: i <= s.wizardStep ? PRIMARY : 'var(--invoicecloud-utility-neutral-50)' }));
  const stepperPct = (s.wizardStep / (stepNames.length - 1)) * 100;

  const setupPathIsUpload = s.setupPath === 'upload';
  const setupPathIsManual = s.setupPath === 'manual';
  const setupPathBg = (active: boolean) => (active ? 'var(--invoicecloud-primary-tint)' : '#fff');
  const setupPathBorder = (active: boolean) => (active ? 'var(--invoicecloud-primary)' : 'var(--invoicecloud-surface-default-border)');
  const setupPathSummaryLabel = s.setupPath === 'upload' ? 'Upload biller data' : s.setupPath === 'manual' ? 'Select billing categories' : 'Not set';
  const setupPathAiRationale = s.setupPath === 'upload' ? "We'll use your uploaded data to pre-fill your payment experience settings." : s.setupPath === 'manual' ? 'You answered manually \u2014 we still recommend AI defaults for anything left blank.' : 'Choose how you\u2019d like to configure your payment experience.';
  const csvSummaryLabel = s.csvFileName ? `Loaded ${s.csvFileName}` : 'No file uploaded yet';
  const showUploadPicker = !s.analyzingUpload && !s.chatActive;
  const hasWebsiteForBrand = s.website.trim().length > 0;
  const livePreviewBrand = buildBrand(s);
  const livePreviewSwatches = [livePreviewBrand.primary, livePreviewBrand.secondary, livePreviewBrand.accent];
  const chatVertical: VerticalId = s.vertical ?? 'other';
  const chatQuestions = VERTICAL_QUESTIONS[chatVertical];
  const chatSuggestions = VERTICAL_SUGGESTIONS[chatVertical];
  const chatLog = chatQuestions.slice(0, s.chatStep).map((q, i) => ({ question: q, answer: s.chatAnswers[i], editing: s.editingChatIndex === i, onEdit: () => editChatAnswerClick(i) }));
  const chatHasCurrentQuestion = s.chatStep < 4 && s.editingChatIndex === null;
  const chatComplete = s.chatStep >= 4;
  const chatDraftEmpty = !s.chatDraft.trim();
  const chatReviewRows = chatQuestions.map((q, i) => ({ question: q, answer: s.chatAnswers[i] || 'Not answered', rationale: CHAT_RATIONALES[i], editing: s.editingChatIndex === i, onEdit: () => editChatAnswerClick(i) }));
  const verticalAiRationale = s.vertical === 'other' ? 'Based on your description, we\u2019re tailoring compliance checks and suggestions to your business.' : `We\u2019ll tailor terminology, fees and compliance checks for the ${(VERTICALS.find((v) => v.id === s.vertical) || { label: '' }).label} vertical.`;
  const locationAiRationale = s.selectedStates.length ? `We pulled compliance requirements for ${s.selectedStates.length} state${s.selectedStates.length > 1 ? 's' : ''}, including any state-specific AutoPay restrictions.` : 'Select at least one state so we can surface the right compliance requirements.';

  const statesLabel = s.selectedStates.length ? s.selectedStates.join(', ') : 'your area';
  const analyzeLabels = [
    'Analyzing your business profile',
    `Checking payment compliance for ${statesLabel}`,
    s.skipWebsite || !s.website ? 'Applying smart brand defaults' : `Scanning ${s.website} for brand assets`,
  ];
  const analyzeStages = analyzeLabels.map((lbl, i) => {
    // Every step settles to a terminal look once the run finishes: done on success, or a stopped
    // warning marker if the run errored out — never a spinner that outlives the run.
    const done = s.analysisComplete || s.analyzeStage > i;
    const failed = !done && !!s.orchestrationError;
    const active = !done && !failed && s.analyzeStage === i;
    return {
      label: lbl,
      bg: done ? 'var(--invoicecloud-intent-success-background)' : failed ? 'var(--invoicecloud-intent-warning-background)' : active ? TINT : 'var(--invoicecloud-utility-neutral-05)',
      opacity: done || active || failed ? 1 : 0.5,
      iconSrc: done ? asset('assets/icons/Checkmark.svg') : failed ? asset('assets/icons/Warning.svg') : asset('assets/icons/Spinner.svg'),
      spin: done || failed ? 'none' : 'spin 1s linear infinite',
    };
  });

  const swatches = [brand.primary, brand.secondary, brand.accent, '#1c1c1c'];
  const domain = domainFromWebsite(s.website);
  const logoFetchUrl = logoUrlForDomain(domain);
  const showUploadedLogo = !!s.logoDataUrl;
  const showFetchedLogo = !showUploadedLogo && s.logoFetchOk === true && !s.skipWebsite;
  const showInitialsLogo = !showUploadedLogo && !showFetchedLogo;
  const isFlorida = (compliance.states || []).includes('Florida');
  const complianceByState = (compliance.states || []).map((name) => ({ state: name, categories: (compliance.byState || {})[name] || [], expanded: s.expandedCompliance.includes(name), onToggle: () => toggleComplianceState(name) }));

  const curatedStatements = VERTICAL_STATEMENTS[s.vertical ?? 'insurance'];
  const statementSource = curatedStatements ?? STATEMENTS;
  const primaryStatement = statementSource.find((st) => st.status === 'Due');
  const amount = curatedStatements ? (primaryStatement?.amount ?? s.amount) : s.amount;
  const heroDueText = curatedStatements ? (primaryStatement?.date ?? 'Due Aug 4') : 'Due Aug 4';
  // Payer sees a service fee only when the biller charges one; 'absorb'/'mixed'/'unsure' show none.
  const serviceFeeApplies = s.feeHandling === 'charge';
  const serviceFee = serviceFeeApplies ? SERVICE_FEE : 0;
  const total = amount + serviceFee;
  const nonBankDisabledForAutopay = isFlorida && s.autopayOptIn;
  const acceptedMethodTypes = s.acceptedMethods.length ? s.acceptedMethods : ['card', 'ach'];
  const ALL_METHOD_TYPES: { id: MethodType; label: string }[] = [
    { id: 'card', label: 'Card' },
    { id: 'bank', label: 'Bank account' },
    { id: 'applepay', label: 'Apple Pay' },
    { id: 'googlepay', label: 'Google Pay' },
    { id: 'paypal', label: 'PayPal' },
  ];
  let methodTypesBase = ALL_METHOD_TYPES.filter((mt) => acceptedMethodTypes.includes(mt.id === 'bank' ? 'ach' : mt.id));
  if (!methodTypesBase.length) methodTypesBase = [{ id: 'card', label: 'Card' }, { id: 'bank', label: 'Bank account' }];
  const methodTypes = methodTypesBase.map((mt) => { const disabled = mt.id !== 'bank' && nonBankDisabledForAutopay; return { ...mt, disabled, opacity: disabled ? 0.5 : 1, onSelect: disabled ? () => {} : () => selectMethodType(mt.id) }; });
  const methodTypeIsWallet = (['applepay', 'googlepay', 'paypal'] as MethodType[]).includes(s.methodType);
  const methodTypeWalletLabel = ({ applepay: 'Apple Pay', googlepay: 'Google Pay', paypal: 'PayPal' } as Record<string, string>)[s.methodType] || '';

  const labelOf = (map: Record<string, string>, key: string) => map[key] || key;
  const reminderChannelLabel = labelOf({ email: 'Email', text: 'Text (SMS)', both: 'Both', none: 'None' }, s.reminderChannel);
  const feeHandlingLabel = labelOf({ absorb: 'We absorb all fees', charge: 'Convenience fee charged to customers', mixed: 'Different rules per payment type', unsure: 'Not decided yet' }, s.feeHandling);
  const acceptedMethodsLabel = s.acceptedMethods.map((id) => labelOf({ ach: 'ACH', card: 'Cards', applepay: 'Apple Pay', googlepay: 'Google Pay', paypal: 'PayPal', other: 'Other' }, id)).join(', ') || 'None selected';
  const shownMethodTypeIds = new Set(methodTypesBase.map((mt) => mt.id));
  const acceptedMethodChips = [{ id: 'applepay', label: 'Apple Pay' }, { id: 'googlepay', label: 'Google Pay' }, { id: 'paypal', label: 'PayPal' }, { id: 'other', label: 'Other' }].filter((c) => s.acceptedMethods.includes(c.id) && !shownMethodTypeIds.has(c.id as MethodType));

  const statements = statementSource.map((st) => ({ ...st, label: `${docLabels.numberLabel} ${docLabels.numberPrefix}${st.id}`, onClick: () => viewStatement(st.id), badgeBg: st.status === 'Due' ? 'var(--invoicecloud-intent-warning-background)' : 'var(--invoicecloud-intent-success-background)', badgeColor: st.status === 'Due' ? 'var(--invoicecloud-intent-warning)' : 'var(--invoicecloud-intent-success)' }));
  const currentStatements = statements.filter((st) => st.status === 'Due');
  const pastStatements = statements.filter((st) => st.status !== 'Due');
  const shownStatements = s.statementTab === 'current' ? currentStatements : pastStatements;
  const statementTabs = [{ id: 'current' as const, label: 'Current', count: currentStatements.length }, { id: 'past' as const, label: 'Past', count: pastStatements.length }].map((t) => ({ ...t, selected: s.statementTab === t.id, onSelect: () => setStatementTab(t.id) }));
  const viewingStatement = (() => {
    const st = statementSource.find((x) => x.id === s.viewingStatementId);
    if (!st) return null;
    const lineMap = lineLabels as unknown as Record<string, string>;
    return { ...st, docLabel: docLabels.docLabel, numberLabel: docLabels.numberLabel, personLabel: docLabels.personLabel, periodLabel: docLabels.periodLabel, totalLabel: docLabels.totalLabel, issuerLabel: docLabels.issuerLabel, numberDisplay: `${docLabels.numberPrefix}${st.id}`, amountFormatted: st.amount.toFixed(2), breakdown: st.breakdown.map((b) => ({ label: labelOf(lineMap, b.key), amountFormatted: b.amount.toFixed(2) })), isDue: st.status === 'Due' };
  })();

  const dataBadgeLabel = s.csvFileName ? 'Using your uploaded data' : 'Using sample data';
  const dataBadgeBg = s.csvFileName ? 'var(--invoicecloud-intent-success-background)' : 'var(--invoicecloud-intent-neutral-background)';
  const dataBadgeColor = s.csvFileName ? 'var(--invoicecloud-intent-success)' : 'var(--invoicecloud-intent-neutral)';
  const scenarioCards = [{ id: 'payment', label: 'Make a Payment' }, { id: 'history', label: 'View Account History' }, { id: 'communication', label: 'Communication Preferences' }, { id: 'complex', label: 'Complex Scenario' }].map((c) => ({ ...c, selected: s.previewScenario === c.id, onSelect: () => selectScenario(c.id) }));
  const historyRows = [
    { date: 'Jun 1', desc: 'Payment received', amount: s.accountNumber ? `Acct ....${s.accountNumber.slice(-4)}` : '$112.15', status: 'Completed' },
    { date: 'May 1', desc: 'Payment received', amount: '$108.40', status: 'Completed' },
    { date: 'Apr 3', desc: 'AutoPay enrollment', amount: '-', status: 'Completed' },
    { date: 'Mar 1', desc: 'Payment received', amount: '$104.90', status: 'Completed' },
  ];
  const previewMaxWidth = s.previewDevice === 'mobile' ? '390px' : '1000px';
  const previewShellMaxWidth = '1000px';
  const scenarioGridCols = 'repeat(4,1fr)';
  const previewAutopayStatusLabel = s.previewAutopayEnrolled ? `Enrolled \u2014 next charge $${amount.toFixed(2)} on Aug 4, 2026` : 'Not enrolled';
  const previewPaperlessStatusLabel = s.previewPaperlessEnrolled ? 'Enrolled \u2014 e-statements only' : 'Not enrolled \u2014 save paper, get bills faster';
  const siteSlug = (s.bizName || 'yourbusiness').toLowerCase().replace(/[^a-z0-9]+/g, '') || 'yourbusiness';
  const accountEmail = s.billerAccountEmail || `demo@${siteSlug}.com`;
  // Rendered from the accepted draft so a proposed heading / primary-action label change is actually
  // reflected in the preview once accepted — keeping the preview and the proposal summary in agreement.
  const previewHeading = s.backendDraft?.definition.content.heading?.trim() ?? '';
  const primaryActionLabel = s.backendDraft?.definition.ui?.actions?.[0]?.label?.trim() || 'Pay Now';
  const billingCategories = s.backendDraft?.definition.billing?.categories ?? [];

  const guestCheckoutAllowedLabel = s.guestCheckoutAllowed ? 'Allowed' : 'Not allowed';
  const offerAutopayLabel = s.offerAutopay ? 'Yes' : 'No';
  const enrollDuringPaymentLabel = s.offerAutopay ? (s.enrollDuringPayment ? 'Yes' : 'No') : 'N/A';
  const offerPaperlessLabel = s.offerPaperless ? 'Yes' : 'No';
  const selfServiceHistoryLabel = s.selfServiceHistory ? 'Yes' : 'No';
  const selfServiceUpdateLabel = s.selfServiceUpdate ? 'Yes' : 'No';
  const verticalSummaryLabel = (VERTICALS.find((v) => v.id === s.vertical) || { label: 'Not set' }).label;
  const locationSummaryLabel = `${s.bizName || 'Unnamed business'} - ${s.selectedStates.length ? s.selectedStates.join(', ') : 'No states selected'}`;
  const brandSourceLabel = brand.colorsFromLogo ? `Colors extracted from the logo at ${s.website}` : s.website && !s.skipWebsite ? `Logo colors unavailable - using a simulated palette for ${s.website}` : 'Simulated default palette for this vertical';

  const reminderOptions = [{ id: 'email', label: 'Email' }, { id: 'text', label: 'Text (SMS)' }, { id: 'both', label: 'Both' }, { id: 'none', label: 'None' }].map((r) => ({ ...r, selected: s.reminderChannel === r.id, onSelect: () => patch({ reminderChannel: r.id, editingSection: null }) }));
  const methodLabels: Record<string, string> = { ach: 'ACH', card: 'Credit/Debit Cards', applepay: 'Apple Pay', googlepay: 'Google Pay', paypal: 'PayPal', other: 'Other' };
  const availablePaymentCapabilities = s.backendDraft?.definition.enabled_payment_capabilities ?? ['card', 'ach'];
  const methodOptions = availablePaymentCapabilities.map(id => ({ id, label: methodLabels[id] ?? id, selected: s.acceptedMethods.includes(id), onToggle: () => toggleAcceptedMethod(id) }));
  const feeOptions = [{ id: 'absorb', label: 'We will absorb all fees' }, { id: 'charge', label: 'Charge a convenience/service fee to customers' }, { id: 'mixed', label: 'Different fee rules for different payment types' }, { id: 'unsure', label: "I'm not sure yet" }].map((f) => ({ ...f, selected: s.feeHandling === f.id, onSelect: () => patch({ feeHandling: f.id, editingSection: null }) }));

  const lobsView = s.lobs.map((l) => {
    const v = VERTICALS.find((vv) => vv.id === l.vertical);
    return { ...l, verticalLabel: v?.label || 'Business', statesLabel: (l.compliance?.states || []).join(', ') || (l.selectedStates || []).join(', ') || 'No states set', hasAccountNumber: !!l.accountNumber, badgeColor: l.brand ? l.brand.primary : '#085368', statusLabel: l.published ? 'Live' : 'Draft', onPreview: () => previewLob(l), onEdit: () => editLob(l) };
  });

  const liveLobCount = s.lobs.filter((l) => l.published).length;
  const dashNav: { id: DashboardSection; label: string; icon: string }[] = [
    { id: 'home', label: 'Home', icon: 'Home' },
    { id: 'lob', label: 'Lines of Business', icon: 'Dollar' },
    { id: 'billing', label: 'Billing', icon: 'BarChart' },
    { id: 'settings', label: 'Settings', icon: 'Cog' },
  ];
  const navItemBase = 'display:flex;align-items:center;gap:var(--invoicecloud-spacing-xs);padding:10px 12px;border-radius:8px;font-size:14px;cursor:pointer;margin-bottom:4px';
  const navItemActive = ';background:var(--invoicecloud-primary-tint);color:var(--invoicecloud-primary);font-weight:500';
  const navItemInactive = ';color:var(--invoicecloud-utility-neutral-70)';
  const goSection = (id: DashboardSection) => patch({ dashboardSection: id });
  const renderNavItem = ({ id, label, icon }: { id: DashboardSection; label: string; icon: string }) => (
    <div
      key={id}
      role="button"
      tabIndex={0}
      aria-current={s.dashboardSection === id ? 'page' : undefined}
      onClick={() => goSection(id)}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); goSection(id); } }}
      style={css(navItemBase + (s.dashboardSection === id ? navItemActive : navItemInactive))}
    >
      <img src={asset(`assets/icons/${icon}.svg`)} alt="" style={css('width:18px;height:18px')} />{label}
    </div>
  );

  const settingsRow = (label: string, value: string) => (
    <div style={css('display:flex;justify-content:space-between;gap:16px;padding:12px 0;border-bottom:1px solid var(--invoicecloud-surface-default-border);font-size:14px')}>
      <span style={css('color:var(--invoicecloud-utility-neutral-70)')}>{label}</span>
      <span style={css('font-weight:500;text-align:right')}>{value}</span>
    </div>
  );
  const helpLinks = [
    { title: 'Contact support', desc: 'Email our onboarding team and we\u2019ll respond within one business day.', href: 'mailto:support@pronto.example', cta: 'support@pronto.example' },
    { title: 'Documentation', desc: 'Guides for configuring lines of business, branding, and publishing.', href: 'https://pronto.eastus2.cloudapp.azure.com/', cta: 'Open docs \u2192' },
    { title: 'View your live payer site', desc: s.deployment?.published_url ? 'Open the payer experience you published.' : 'Publish a line of business to get a live payer link.', href: s.deployment?.published_url ?? 'https://pronto.eastus2.cloudapp.azure.com/', cta: 'Open payer site \u2192' },
  ];

  const linesOfBusinessGrid = (
    <>
      <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-s)')}>
        <h3 style={css('font-size:16px')}>Lines of business</h3>
        <button type="button" onClick={addLob} style={css('display:flex;align-items:center;gap:6px;background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:8px 16px;font-size:13px;font-weight:700;cursor:pointer')}><img src={asset('assets/icons/Plus.svg')} alt="" style={css('width:14px;height:14px;filter:brightness(0) invert(1)')} />Add line of business</button>
      </div>
      {lobsView.length === 0 ? (
        <div style={css('border:1px dashed var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-l);text-align:center;color:var(--invoicecloud-utility-neutral-70);font-size:14px')}>No lines of business yet. Add one to configure a payer experience.</div>
      ) : (
        <div style={css('display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:var(--invoicecloud-spacing-m)')}>
          {lobsView.map((l) => (
            <div key={l.id} style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m)')}>
              <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                <div style={css(`width:40px;height:40px;border-radius:10px;display:flex;align-items:center;justify-content:center;color:#fff;font-weight:700;background:${l.badgeColor}`)}>{(l.brand && l.brand.initials) || initialsFrom(l.bizName)}</div>
                <div>
                  <div style={css('font-weight:500')}>{l.bizName}</div>
                  <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>{l.verticalLabel}</div>
                </div>
                <div style={css('flex:1')}></div>
                <span style={css(`font-size:11px;font-weight:700;padding:4px 10px;border-radius:4px;background:${l.published ? 'var(--invoicecloud-intent-success-background)' : 'var(--invoicecloud-intent-neutral-background)'};color:${l.published ? 'var(--invoicecloud-intent-success)' : 'var(--invoicecloud-intent-neutral)'}`)}>{l.statusLabel}</span>
              </div>
              <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-s)')}>{l.statesLabel}</div>
              <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs)')}>
                <button type="button" onClick={l.onPreview} style={css('flex:1;background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:8px;font-size:13px;cursor:pointer')}>Preview</button>
                <button type="button" onClick={l.onEdit} style={css('flex:1;background:none;border:1px solid var(--invoicecloud-primary);color:var(--invoicecloud-primary);border-radius:8px;padding:8px;font-size:13px;font-weight:700;cursor:pointer')}>Edit</button>
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  );

  const landingFeatures = [
    { title: 'Compliance-aware', desc: 'We check local payment regulations for every state you operate in before you launch.', iconSrc: asset('assets/icons/DocumentSearch.svg') },
    { title: 'Matches your brand', desc: 'Scan your website to pull colors, logo and fonts automatically.', iconSrc: asset('assets/icons/Magic.svg') },
    { title: 'AutoPay & Paperless built in', desc: 'Payers can enroll in recurring billing and e-statements at checkout.', iconSrc: asset('assets/icons/AutoPay.svg') },
  ];

  const magic = <img src={asset('assets/icons/Magic.svg')} alt="" style={css('width:14px;height:14px;flex:none')} />;

  const yesNo = (onYes: () => void, onNo: () => void, val: boolean, big = true, yesText = 'Yes', noText = 'No') => (
    <div style={css(`display:flex;gap:var(--invoicecloud-spacing-xs);margin-top:var(--invoicecloud-spacing-${big ? 's' : 'xs'})`)}>
      <button type="button" onClick={onYes} style={css(`padding:${big ? '10px 24px' : '8px 18px'};border-radius:8px;font-size:${big ? '14' : '13'}px;cursor:pointer;background:${selBg(val === true)};border:1.5px solid ${selBorder(val === true)}`)}>{yesText}</button>
      <button type="button" onClick={onNo} style={css(`padding:${big ? '10px 24px' : '8px 18px'};border-radius:8px;font-size:${big ? '14' : '13'}px;cursor:pointer;background:${selBg(val === false)};border:1.5px solid ${selBorder(val === false)}`)}>{noText}</button>
    </div>
  );
  const editLink = (onClick: () => void, text = 'Edit', small = false) => (
    <button type="button" onClick={onClick} style={css(`background:none;border:none;color:var(--invoicecloud-primary);font-size:${small ? '12' : '13'}px;font-weight:700;cursor:pointer`)}>{text}</button>
  );
  const cardStyle = css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)');
  const rationaleRow = (text?: string) => (
    <div style={css('display:flex;gap:6px;margin-top:6px;font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>{magic}{text}</div>
  );

  return (
    <div style={css('font-family:var(--invoicecloud-font-family-primary);min-height:100vh;background:var(--invoicecloud-utility-neutral-05);color:var(--invoicecloud-utility-neutral-100);text-wrap:pretty')}>
      <div inert={s.modal !== null} aria-hidden={s.modal !== null}>

      {/* ================= LANDING ================= */}
      {s.screen === 'landing' && (
        <div style={css('display:flex;flex-direction:column;min-height:100vh')}>
          <header style={css('display:flex;align-items:center;justify-content:space-between;padding:var(--invoicecloud-spacing-m) var(--invoicecloud-spacing-l);background:var(--invoicecloud-primary)')}>
            <img src={asset('assets/pronto-logo.svg')} alt="Pronto" style={css('height:22px;filter:brightness(0) invert(1)')} />
            <span style={css('color:#fff;font-size:14px;opacity:.85;font-family:var(--invoicecloud-font-family-mono);letter-spacing:.05em')}>Payment Portal Builder</span>
          </header>
          <section style={css('background:var(--invoicecloud-primary);color:#fff;padding:96px var(--invoicecloud-spacing-l) 128px;text-align:center')}>
            <div style={css('font-size:13px;font-weight:500;letter-spacing:.15em;text-transform:uppercase;color:rgba(255,255,255,.7);margin-bottom:var(--invoicecloud-spacing-s);font-family:var(--invoicecloud-font-family-mono)')}>Pronto - Try It Out</div>
            <h1 style={css('font-size:52px;line-height:1.1;font-weight:900;max-width:780px;margin:0 auto var(--invoicecloud-spacing-m);color:#fff')}>Build your own payment portal. Done tomorrow.</h1>
            <p style={css('font-size:18px;font-weight:300;max-width:560px;margin:0 auto var(--invoicecloud-spacing-l);color:rgba(255,255,255,.85)')}>Answer a few questions about your business. We'll check payment compliance for your area, match your brand, and generate a live preview - in under a minute.</p>
            <button type="button" onClick={goTry} style={css('background:#fff;color:var(--invoicecloud-primary);border:none;border-radius:10px;padding:18px 40px;font-size:16px;font-weight:700;cursor:pointer;box-shadow:var(--invoicecloud-elevation-2)')}>Try It Out</button>
            <div style={css('font-size:13px;color:rgba(255,255,255,.65);margin-top:var(--invoicecloud-spacing-s)')}>No account or credit card required to preview</div>
          </section>
          <section style={css('max-width:1100px;margin:-56px auto 80px;padding:0 var(--invoicecloud-spacing-l);display:grid;grid-template-columns:repeat(3,1fr);gap:var(--invoicecloud-spacing-m);position:relative;flex:1;width:100%')}>
            {landingFeatures.map((f) => (
              <div key={f.title} style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;box-shadow:var(--invoicecloud-card-shadow);padding:var(--invoicecloud-spacing-m)')}>
                <div style={css('width:44px;height:44px;border-radius:10px;background:var(--invoicecloud-primary-tint);display:flex;align-items:center;justify-content:center;margin-bottom:var(--invoicecloud-spacing-s)')}>
                  <img src={f.iconSrc} alt="" style={css('width:22px;height:22px')} />
                </div>
                <h3 style={css('font-size:18px;margin-bottom:var(--invoicecloud-spacing-xs)')}>{f.title}</h3>
                <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70)')}>{f.desc}</p>
              </div>
            ))}
          </section>
          <footer style={css('text-align:center;padding:var(--invoicecloud-spacing-l);font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>Pronto Payment Portal Builder - a guided demo, not a live billing system.</footer>
        </div>
      )}

      {/* ================= WIZARD ================= */}
      {s.screen === 'wizard' && (
        <div style={css('min-height:100vh;display:flex;flex-direction:column;align-items:center;padding:var(--invoicecloud-spacing-xl) var(--invoicecloud-spacing-l)')}>
          <img src={asset('assets/pronto-logo.svg')} alt="Pronto" style={css('height:26px;margin-bottom:var(--invoicecloud-spacing-l)')} />
          <div style={css('width:100%;max-width:640px;background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;box-shadow:var(--invoicecloud-card-shadow);padding:var(--invoicecloud-spacing-l)')}>

            <div role="progressbar" style={css('margin-bottom:var(--invoicecloud-spacing-l)')}>
              <div style={css('position:relative;height:4px;background:var(--invoicecloud-utility-neutral-10);border-radius:4px;margin-bottom:var(--invoicecloud-spacing-s)')}>
                <div style={css(`position:absolute;left:0;top:0;height:4px;border-radius:4px;background:var(--invoicecloud-primary);transition:width .3s ease-in-out;width:${stepperPct}%`)}></div>
              </div>
              <div style={css('display:flex;justify-content:space-between;font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>
                {stepLabels.map((lbl) => <span key={lbl.text} style={css(`font-weight:${lbl.weight};color:${lbl.color}`)}>{lbl.text}</span>)}
              </div>
            </div>

            {s.wizardStep === 0 && (
              <>
                <h2 style={css('margin-bottom:var(--invoicecloud-spacing-xs)')}>What line of business is this for?</h2>
                <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>We'll tailor terminology, fees and compliance checks to your vertical.</p>
                <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s)')}>
                  {verticals.map((v) => (
                    <button key={v.id} type="button" onClick={v.onSelect} style={css(`display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${selBg(v.selected)};border:1.5px solid ${selBorder(v.selected)}`)}>
                      <div style={css('width:40px;height:40px;flex:none;border-radius:10px;background:var(--invoicecloud-primary-tint);display:flex;align-items:center;justify-content:center')}>
                        <img src={v.iconSrc} alt="" style={css('width:20px;height:20px')} />
                      </div>
                      <div>
                        <div style={css('font-weight:500;font-size:16px')}>{v.label}</div>
                        <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70)')}>{v.desc}</div>
                      </div>
                    </button>
                  ))}
                </div>
                {s.vertical === 'other' && (
                  <>
                    <label style={css('display:block;font-size:13px;font-weight:500;margin-top:var(--invoicecloud-spacing-m);margin-bottom:4px')}>What type of organization is this?</label>
                    <input type="text" value={s.otherVerticalDescription} onChange={setOtherVerticalDescription} placeholder="e.g. HOA, Property Management, Nonprofit, Membership or Club" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px;font-family:var(--invoicecloud-font-family-primary)')} />
                  </>
                )}
              </>
            )}

            {s.wizardStep === 1 && (
              <>
                <h2 style={css('margin-bottom:var(--invoicecloud-spacing-xs)')}>Business Details</h2>
                <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>The states you operate in help us surface the right autopay, refund and fee-disclosure rules.</p>
                <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:var(--invoicecloud-spacing-xxs)')}>Business name</label>
                <input type="text" value={s.bizName} onChange={setBizName} placeholder="e.g. Your Organization" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:16px;margin-bottom:var(--invoicecloud-spacing-m)')} />
                <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:var(--invoicecloud-spacing-xxs)')}>Which states do you conduct business in?</label>
                <input type="text" value={s.stateSearch} onChange={setStateSearch} placeholder="Search states..." style={css('width:100%;padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px;margin-bottom:var(--invoicecloud-spacing-xs)')} />
                <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-xs);max-height:220px;overflow-y:auto;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-xs)')}>
                  {stateOptions.map((st) => (
                    <button key={st.name} type="button" onClick={st.onToggle} style={css(`display:flex;align-items:center;justify-content:space-between;text-align:left;padding:10px 14px;border-radius:8px;cursor:pointer;background:${selBg(st.selected)};border:1.5px solid ${selBorder(st.selected)}`)}>
                      <span style={css('font-size:14px;font-weight:500')}>{st.name}</span>
                      {st.selected && <img src={asset('assets/icons/Checkmark.svg')} alt="" style={css('width:16px;height:16px')} />}
                    </button>
                  ))}
                  {stateOptions.length === 0 && (
                    <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-60);text-align:center;padding:14px')}>No matching states</div>
                  )}
                </div>

                <label style={css('display:block;font-size:13px;font-weight:500;margin-top:var(--invoicecloud-spacing-l);margin-bottom:4px')}>Website <span style={css('font-weight:400;color:var(--invoicecloud-utility-neutral-60)')}>(optional)</span></label>
                <input type="text" value={s.website} onChange={setWebsite} placeholder="www.yourbusiness.com" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:16px;margin-bottom:var(--invoicecloud-spacing-xs)')} />
                <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-bottom:var(--invoicecloud-spacing-m)')}>If you have one, we'll scan it to match your brand on the next steps.</div>
                <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:var(--invoicecloud-spacing-xs)')}>How would you like to set up your payment experience?</label>
                <div style={css('display:grid;grid-template-columns:1fr 1fr;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-m)')}>
                  <button type="button" onClick={setSetupPathUpload} style={css(`text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${setupPathBg(setupPathIsUpload)};border:1.5px solid ${setupPathBorder(setupPathIsUpload)}`)}>
                    <div style={css('font-weight:500;font-size:14px;margin-bottom:2px')}>Upload biller data</div>
                    <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Add any format of 30 days of invoice data and we'll scan it to understand how your business works.</div>
                  </button>
                  <button type="button" onClick={setSetupPathManual} style={css(`text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${setupPathBg(setupPathIsManual)};border:1.5px solid ${setupPathBorder(setupPathIsManual)}`)}>
                    <div style={css('font-weight:500;font-size:14px;margin-bottom:2px')}>Select billing categories</div>
                    <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Name what you bill for; agents will infer the remaining policy settings.</div>
                  </button>
                </div>

                {setupPathIsUpload && (
                  <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s)')}>
                    {showUploadPicker && (
                      <>
                        <input type="file" accept=".csv,.xlsx,.xls,.json,.yaml,.yml,.pdf" onChange={onCsvFile} style={css('font-size:13px;margin-bottom:var(--invoicecloud-spacing-s)')} />
                        {s.importedFields.length > 0 && (
                          <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                            <div style={css('font-size:13px;font-weight:500;margin-bottom:var(--invoicecloud-spacing-xs)')}>Recognized values</div>
                            {s.importedFields.map((im, i) => (
                              <div key={i} style={css('display:flex;justify-content:space-between;font-size:13px;padding:4px 0')}>
                                <span style={css('color:var(--invoicecloud-utility-neutral-70)')}>{im.label}</span><span style={css('font-weight:500')}>{im.value}</span>
                              </div>
                            ))}
                          </div>
                        )}
                        <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>No file yet? We'll recommend settings for you instead.</div>
                      </>
                    )}
                    {s.analyzingUpload && (
                      <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);color:var(--invoicecloud-utility-neutral-70);font-size:14px')}>
                        <img src={asset('assets/icons/Spinner.svg')} alt="" style={css('width:18px;height:18px;animation:spin 1s linear infinite')} />An agent is analyzing your data&hellip;
                      </div>
                    )}
                  </div>
                )}

                {setupPathIsManual && (
                  <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-s)')}>
                    <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:6px')}>Billing categories</label>
                    <input type="text" value={s.chatAnswers[0] ?? ''} onChange={(event) => patch(st => ({ chatAnswers: [event.target.value, ...st.chatAnswers.slice(1)] }))} placeholder={VERTICAL_SUGGESTIONS[chatVertical][0]} style={css('width:100%;padding:10px 12px;border-radius:8px;border:1px solid var(--invoicecloud-surface-default-border);font-size:13px')} />
                    <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-top:6px')}>Separate categories with commas. Cadence, late-payment rules, and payment terms will be applied as explicit agent assumptions.</div>
                  </div>
                )}

                {false && s.chatActive && (
                  <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-s)')}>
                    {chatLog.map((msg, i) => (
                      <div key={i}>
                        <div style={css('display:flex;gap:8px;margin-bottom:6px')}>
                          <div style={css('width:24px;height:24px;border-radius:50%;background:var(--invoicecloud-primary-tint);flex:none;display:flex;align-items:center;justify-content:center')}><img src={asset('assets/icons/Magic.svg')} alt="" style={css('width:13px;height:13px')} /></div>
                          <div style={css('background:var(--invoicecloud-slate-10);border-radius:10px;padding:8px 12px;font-size:13px;max-width:80%')}>{msg.question}</div>
                        </div>
                        {msg.editing ? (
                          <div style={css('display:flex;gap:8px;justify-content:flex-end;margin-left:32px')}>
                            <input type="text" value={s.chatDraft} onChange={setChatDraft} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); saveChatAnswerEdit(); } }} style={css('flex:1;padding:8px 12px;border-radius:10px;border:1px solid var(--invoicecloud-primary);font-size:13px')} />
                            <button type="button" onClick={saveChatAnswerEdit} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:8px 14px;font-size:13px;font-weight:700;cursor:pointer')}>Save</button>
                          </div>
                        ) : (
                          <div style={css('display:flex;gap:6px;justify-content:flex-end;align-items:flex-start;margin-left:32px')}>
                            <div style={css('background:var(--invoicecloud-primary);color:#fff;border-radius:10px;padding:8px 12px;font-size:13px;max-width:80%')}>{msg.answer}</div>
                            <button type="button" onClick={msg.onEdit} aria-label="Edit answer" style={css('background:none;border:none;cursor:pointer;padding:4px;flex:none')}><img src={asset('assets/icons/Cog.svg')} alt="" style={css('width:14px;height:14px;opacity:.5')} /></button>
                          </div>
                        )}
                      </div>
                    ))}
                    {chatHasCurrentQuestion && (
                      <>
                        <div style={css('display:flex;gap:8px;margin-bottom:6px')}>
                          <div style={css('width:24px;height:24px;border-radius:50%;background:var(--invoicecloud-primary-tint);flex:none;display:flex;align-items:center;justify-content:center')}><img src={asset('assets/icons/Magic.svg')} alt="" style={css('width:13px;height:13px')} /></div>
                          <div style={css('background:var(--invoicecloud-slate-10);border-radius:10px;padding:8px 12px;font-size:13px;max-width:80%')}>{chatQuestions[s.chatStep]}</div>
                        </div>
                        <div style={css('display:flex;gap:8px;margin-left:32px')}>
                          <input type="text" value={s.chatDraft} onChange={setChatDraft} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); submitChatAnswer(); } }} placeholder="Type your answer&hellip;" style={css('flex:1;padding:10px 12px;border-radius:10px;border:1px solid var(--invoicecloud-surface-default-border);font-size:13px')} />
                          <button type="button" onClick={submitChatAnswer} disabled={chatDraftEmpty} style={css(`background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 16px;font-size:13px;font-weight:700;cursor:pointer;opacity:${chatDraftEmpty ? 0.5 : 1}`)}>Enter</button>
                        </div>
                        {chatDraftEmpty && chatSuggestions[s.chatStep] && (
                          <button type="button" onClick={() => applyChatSuggestion(chatSuggestions[s.chatStep])} style={css('display:flex;align-items:center;gap:6px;margin-left:32px;background:var(--invoicecloud-primary-tint);border:1px solid var(--invoicecloud-primary);color:var(--invoicecloud-primary);border-radius:10px;padding:8px 12px;font-size:12px;text-align:left;cursor:pointer')}>
                            <img src={asset('assets/icons/Magic.svg')} alt="" style={css('width:12px;height:12px;flex:none')} />
                            <span><strong>Suggested:</strong> {chatSuggestions[s.chatStep]}</span>
                          </button>
                        )}
                      </>
                    )}
                    {chatComplete && (
                      <div style={css('font-size:13px;color:var(--invoicecloud-intent-success)')}>&#10003; Thanks &mdash; that's everything we need for now.</div>
                    )}
                  </div>
                )}
              </>
            )}

            {s.wizardStep === 2 && (
              <>
                <h2 style={css('margin-bottom:var(--invoicecloud-spacing-xs)')}>Brand Details</h2>

                {hasWebsiteForBrand ? (
                  <>
                    <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>Here's what we scanned from <strong>{s.website}</strong>. You can edit any of it.</p>

                    <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                      <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                        <div style={css('font-size:14px;font-weight:500')}>Logo</div>
                        <button type="button" onClick={editBrandLogoClick} style={css('background:none;border:none;color:var(--invoicecloud-primary);font-size:13px;font-weight:700;cursor:pointer')}>Edit</button>
                      </div>
                      <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-s)')}>
                        {showUploadedLogo && <img src={s.logoDataUrl ?? ''} alt="" style={css('width:44px;height:44px;border-radius:10px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border)')} />}
                        {showFetchedLogo && <img src={logoFetchUrl} alt="" style={css('width:44px;height:44px;border-radius:10px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border);background:#fff')} />}
                        {showInitialsLogo && <div style={css(`width:44px;height:44px;border-radius:10px;display:flex;align-items:center;justify-content:center;color:#fff;font-weight:700;background:${livePreviewBrand.primary}`)}>{livePreviewBrand.initials}</div>}
                      </div>
                      {s.editingSection === 'brandlogo' && (
                        <div style={css('margin-top:var(--invoicecloud-spacing-s)')}>
                          {showUploadedLogo ? (
                            <button type="button" onClick={clearLogo} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:8px 14px;font-size:13px;cursor:pointer')}>Remove uploaded logo</button>
                          ) : (
                            <input type="file" accept="image/*" onChange={setLogoFile} style={css('font-size:13px')} />
                          )}
                        </div>
                      )}
                    </div>

                    <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                      <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                        <div style={css('font-size:14px;font-weight:500')}>Colors</div>
                        <button type="button" onClick={editBrandColorsClick} style={css('background:none;border:none;color:var(--invoicecloud-primary);font-size:13px;font-weight:700;cursor:pointer')}>Edit</button>
                      </div>
                      <div style={css('display:flex;gap:8px;margin-top:var(--invoicecloud-spacing-s)')}>
                        {livePreviewSwatches.map((hex, i) => <div key={i} style={css(`width:32px;height:32px;border-radius:8px;border:1px solid rgba(0,0,0,.08);background:${hex}`)} title={hex}></div>)}
                      </div>
                      {s.editingSection === 'brandcolors' && (
                        <div style={css('margin-top:var(--invoicecloud-spacing-s)')}>
                          <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                            <button type="button" onClick={colorChoiceAutoClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.colorChoice === 'auto')};border:1.5px solid ${selBorder(s.colorChoice === 'auto')}`)}>Use scraped colors</button>
                            <button type="button" onClick={colorChoiceCustomClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.colorChoice === 'custom')};border:1.5px solid ${selBorder(s.colorChoice === 'custom')}`)}>Override colors</button>
                          </div>
                          {s.colorChoice === 'custom' && (
                            <div style={css('display:flex;gap:var(--invoicecloud-spacing-m)')}>
                              <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Primary<br /><input type="color" value={s.customPrimary} onChange={setCustomPrimary} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                              <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Secondary<br /><input type="color" value={s.customSecondary} onChange={setCustomSecondary} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                              <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Accent<br /><input type="color" value={s.customAccent} onChange={setCustomAccent} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                            </div>
                          )}
                        </div>
                      )}
                    </div>

                    <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s)')}>
                      <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                        <div style={css('font-size:14px;font-weight:500')}>Font</div>
                        <button type="button" onClick={editBrandFontClick} style={css('background:none;border:none;color:var(--invoicecloud-primary);font-size:13px;font-weight:700;cursor:pointer')}>Edit</button>
                      </div>
                      <div style={css('font-size:14px;margin-top:6px')}>{livePreviewBrand.font}</div>
                      {s.editingSection === 'brandfont' && (
                        <div style={css('margin-top:var(--invoicecloud-spacing-s)')}>
                          <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                            <button type="button" onClick={fontChoiceAutoClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.fontChoice === 'auto')};border:1.5px solid ${selBorder(s.fontChoice === 'auto')}`)}>Use scraped font</button>
                            <button type="button" onClick={fontChoiceCustomClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.fontChoice !== 'auto')};border:1.5px solid ${selBorder(s.fontChoice !== 'auto')}`)}>Choose a Google Font</button>
                          </div>
                          {s.fontChoice !== 'auto' && (
                            <select value={s.fontChoice} onChange={setFontChoice} style={css('padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px')}>
                              {GOOGLE_FONTS.map((gf) => <option key={gf} value={gf}>{gf}</option>)}
                            </select>
                          )}
                        </div>
                      )}
                    </div>
                  </>
                ) : (
                  <>
                    <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>No website on file &mdash; choose your logo, colors and font manually.</p>
                    <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Upload your logo</label>
                    {showUploadedLogo ? (
                      <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s)')}>
                        <img src={s.logoDataUrl ?? ''} alt="Uploaded logo" style={css('width:44px;height:44px;border-radius:10px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border)')} />
                        <button type="button" onClick={clearLogo} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:8px 14px;font-size:13px;cursor:pointer')}>Remove</button>
                      </div>
                    ) : (
                      <input type="file" accept="image/*" onChange={setLogoFile} style={css('font-size:13px')} />
                    )}

                    <label style={css('display:block;font-size:13px;font-weight:500;margin-top:var(--invoicecloud-spacing-m);margin-bottom:4px')}>Brand colors</label>
                    <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                      <button type="button" onClick={colorChoiceAutoClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.colorChoice === 'auto')};border:1.5px solid ${selBorder(s.colorChoice === 'auto')}`)}>Pick for Me</button>
                      <button type="button" onClick={colorChoiceCustomClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.colorChoice === 'custom')};border:1.5px solid ${selBorder(s.colorChoice === 'custom')}`)}>Choose Colors</button>
                    </div>
                    {s.colorChoice === 'custom' && (
                      <div style={css('display:flex;gap:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-s)')}>
                        <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Primary<br /><input type="color" value={s.customPrimary} onChange={setCustomPrimary} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                        <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Secondary<br /><input type="color" value={s.customSecondary} onChange={setCustomSecondary} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                        <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Accent<br /><input type="color" value={s.customAccent} onChange={setCustomAccent} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                      </div>
                    )}

                    <label style={css('display:block;font-size:13px;font-weight:500;margin-top:var(--invoicecloud-spacing-s);margin-bottom:4px')}>Font</label>
                    <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                      <button type="button" onClick={fontChoiceAutoClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.fontChoice === 'auto')};border:1.5px solid ${selBorder(s.fontChoice === 'auto')}`)}>Pick for Me</button>
                      <button type="button" onClick={fontChoiceCustomClick} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.fontChoice !== 'auto')};border:1.5px solid ${selBorder(s.fontChoice !== 'auto')}`)}>Choose a Google Font</button>
                    </div>
                    {s.fontChoice !== 'auto' && (
                      <select value={s.fontChoice} onChange={setFontChoice} style={css('padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px')}>
                        {GOOGLE_FONTS.map((gf) => <option key={gf} value={gf}>{gf}</option>)}
                      </select>
                    )}
                  </>
                )}
              </>
            )}

            <div style={css('display:flex;justify-content:space-between;margin-top:var(--invoicecloud-spacing-l)')}>
              <button type="button" onClick={prevStep} disabled={s.wizardStep === 0} style={css(`background:none;border:none;color:var(--invoicecloud-primary);font-weight:700;font-size:15px;cursor:pointer;visibility:${s.wizardStep === 0 ? 'hidden' : 'visible'}`)}>&larr; Back</button>
              {isLastWizardStep ? (
                <button type="button" onClick={runAnalysis} disabled={!wizardCanProceed} style={wizardCtaStyle(wizardCanProceed)}>Build My Preview</button>
              ) : (
                <button type="button" onClick={nextStep} disabled={!wizardCanProceed} style={wizardCtaStyle(wizardCanProceed)}>Continue</button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* ================= ANALYZING ================= */}
      {s.screen === 'analyzing' && (
        <div style={css('min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;padding:var(--invoicecloud-spacing-l)')}>
          <div role="img" aria-label="Pronto" style={css('position:relative;width:232px;height:60px;margin-bottom:var(--invoicecloud-spacing-l)')}>
            <svg viewBox="0 0 401 104" style={css('position:absolute;top:0;left:0;width:100%;height:100%;overflow:visible')}>
              {PRONTO_PATHS.map((d, i) => (
                <path key={i} d={d} style={css('fill:none;stroke:#197D00;stroke-width:2;stroke-linejoin:round;stroke-dasharray:1500;stroke-dashoffset:1500;animation:prontoStroke 2.6s ease-in-out infinite')} />
              ))}
            </svg>
            <svg viewBox="0 0 401 104" style={css('position:absolute;top:0;left:0;width:100%;height:100%;overflow:visible;animation:prontoFillReveal 2.6s ease-in-out infinite')}>
              {PRONTO_PATHS.map((d, i) => (
                <path key={i} d={d} style={css('fill:#197D00')} />
              ))}
            </svg>
          </div>

          <h2 style={css('margin-bottom:var(--invoicecloud-spacing-m)')}>{!s.analysisComplete ? 'Building your preview...' : runOutcome(s.agentActivity) === 'failed' ? 'Preview built — some steps failed' : runOutcome(s.agentActivity) === 'warnings' ? 'Completed with warnings — research unavailable, using supplied info' : 'Research completed successfully'}</h2>
          <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s);width:100%;max-width:440px')}>
            {analyzeStages.map((st, i) => (
              <div key={i} style={css(`display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);padding:var(--invoicecloud-spacing-s);border-radius:10px;background:${st.bg};opacity:${st.opacity}`)}>
                <img src={st.iconSrc} alt="" style={css(`width:20px;height:20px;animation:${st.spin}`)} />
                <span style={css('font-size:15px')}>{st.label}</span>
              </div>
            ))}
          </div>
          <AgentActivityPanel activity={s.agentActivity} connection={s.activityConnection} complete={s.analysisComplete || !!s.orchestrationError} />
          {s.analysisComplete && !s.orchestrationError && (() => {
            const outcome = runOutcome(s.agentActivity);
            const palette = outcome === 'failed'
              ? { border: '#b42318', bg: '#fff1f0', text: '#7a271a', button: '#b42318' }
              : outcome === 'warnings'
                ? { border: '#b54708', bg: '#fffaeb', text: '#7a4100', button: '#b54708' }
                : { border: '#197d00', bg: '#f0f9ed', text: '#145c00', button: '#197d00' };
            const completedCount = s.agentActivity.filter(item => item.status === 'completed').length;
            const heading = outcome === 'failed'
              ? 'Some agent tasks failed — review findings before proceeding.'
              : outcome === 'warnings'
                ? `${completedCount} agent tasks completed; some steps returned warnings.`
                : `${completedCount} agent tasks completed.`;
            return (
              <div role="status" style={css(`width:100%;max-width:720px;margin-top:16px;padding:16px;border:1px solid ${palette.border};border-radius:10px;background:${palette.bg};color:${palette.text};text-align:center`)}>
                <strong>{heading}</strong>
                <p style={css('margin:6px 0 12px')}>The orchestration run finished and its results are ready for review.</p>
                <button type="button" onClick={() => patch({ screen: 'results' })} style={css(`border:0;border-radius:8px;padding:10px 18px;background:${palette.button};color:#fff;font-weight:700;cursor:pointer`)}>Review agent findings</button>
              </div>
            );
          })()}
          {s.orchestrationError && (
            <div role="alert" style={css('width:100%;max-width:720px;margin-top:16px;padding:16px;border:1px solid #b42318;border-radius:10px;background:#fff1f0;color:#7a271a')}>
              <strong>We could not finish building this preview.</strong>
              <p style={css('margin:6px 0 12px')}>{s.orchestrationError}</p>
              <button type="button" onClick={() => void runAnalysis()} style={css('border:0;border-radius:8px;padding:10px 16px;background:#7a271a;color:#fff;font-weight:700;cursor:pointer')}>Try again</button>
            </div>
          )}
        </div>
      )}

      {/* ================= RESULTS ================= */}
      {s.screen === 'results' && (
        <div style={css('min-height:100vh;padding:var(--invoicecloud-spacing-xl) var(--invoicecloud-spacing-l);display:flex;flex-direction:column;align-items:center')}>
          <h2 style={css('margin-bottom:var(--invoicecloud-spacing-xxs);font-size:32px')}>Let's Review Things</h2>
          <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-l)')}>Review before we build the live preview.</p>
          <div style={css('width:100%;max-width:900px;display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-m)')}>

            <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m)')}>
              <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                <h3 style={css('font-size:16px')}>Vertical</h3>
                {editLink(() => openReviewSection('vertical'))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{verticalSummaryLabel}</div>
              {rationaleRow(verticalAiRationale)}
              {s.reviewEditingSection === 'vertical' && (
                <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-m)')}>
                  {verticals.map((v) => (
                    <button key={v.id} type="button" onClick={v.onSelect} style={css(`display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${selBg(v.selected)};border:1.5px solid ${selBorder(v.selected)}`)}>
                      <div style={css('width:40px;height:40px;flex:none;border-radius:10px;background:var(--invoicecloud-primary-tint);display:flex;align-items:center;justify-content:center')}><img src={v.iconSrc} alt="" style={css('width:20px;height:20px')} /></div>
                      <div><div style={css('font-weight:500;font-size:16px')}>{v.label}</div><div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70)')}>{v.desc}</div></div>
                    </button>
                  ))}
                  {s.vertical === 'other' && <input type="text" value={s.otherVerticalDescription} onChange={setOtherVerticalDescription} placeholder="What type of organization is this?" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px;font-family:var(--invoicecloud-font-family-primary)')} />}
                  <button type="button" ref={saveBtnRef} onClick={saveVerticalSection} style={css('align-self:flex-start;background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 20px;font-size:14px;font-weight:700;cursor:pointer')}>Save</button>
                </div>
              )}
            </div>

            <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m)')}>
              <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Business Details</h3>

              <div style={cardStyle}>
                <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                  <div style={css('font-size:14px;font-weight:500')}>Business name &amp; states</div>
                  {editLink(() => openReviewSection('location'))}
                </div>
                <div style={css('font-size:14px;margin-top:4px')}>{locationSummaryLabel}</div>
                {rationaleRow(locationAiRationale)}
                {s.reviewEditingSection === 'location' && (
                  <div style={css('margin-top:var(--invoicecloud-spacing-m)')}>
                    <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Business name</label>
                    <input type="text" value={s.bizName} onChange={setBizName} style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:16px;margin-bottom:var(--invoicecloud-spacing-m)')} />
                    <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>States</label>
                    <input type="text" value={s.stateSearch} onChange={setStateSearch} placeholder="Search states..." style={css('width:100%;padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px;margin-bottom:var(--invoicecloud-spacing-xs)')} />
                    <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-xs);max-height:220px;overflow-y:auto;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-m)')}>
                      {stateOptions.map((st) => (
                        <button key={st.name} type="button" onClick={st.onToggle} style={css(`display:flex;align-items:center;justify-content:space-between;text-align:left;padding:10px 14px;border-radius:8px;cursor:pointer;background:${selBg(st.selected)};border:1.5px solid ${selBorder(st.selected)}`)}>
                          <span style={css('font-size:14px;font-weight:500')}>{st.name}</span>
                          {st.selected && <img src={asset('assets/icons/Checkmark.svg')} alt="" style={css('width:16px;height:16px')} />}
                        </button>
                      ))}
                      {stateOptions.length === 0 && (
                        <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-60);text-align:center;padding:14px')}>No matching states</div>
                      )}
                    </div>
                    <button type="button" ref={saveBtnRef} onClick={saveLocationSection} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 20px;font-size:14px;font-weight:700;cursor:pointer')}>Save</button>
                  </div>
                )}
              </div>

              <div style={cardStyle}>
                <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                  <div style={css('font-size:14px;font-weight:500')}>Setup path</div>
                  {editLink(() => patch({ editingSection: s.editingSection === 'setuppath' ? null : 'setuppath' }))}
                </div>
                <div style={css('font-size:14px;margin-top:4px')}>{setupPathSummaryLabel}</div>
                {rationaleRow(setupPathAiRationale)}
                {s.editingSection === 'setuppath' && (
                  <div style={css('display:grid;grid-template-columns:1fr 1fr;gap:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-s)')}>
                    <button type="button" onClick={setSetupPathUpload} style={css(`text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${setupPathBg(setupPathIsUpload)};border:1.5px solid ${setupPathBorder(setupPathIsUpload)}`)}><div style={css('font-weight:500;font-size:14px')}>Upload biller data</div></button>
                    <button type="button" onClick={setSetupPathManual} style={css(`text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${setupPathBg(setupPathIsManual)};border:1.5px solid ${setupPathBorder(setupPathIsManual)}`)}><div style={css('font-weight:500;font-size:14px')}>Manually answer questions</div></button>
                  </div>
                )}
              </div>

              {setupPathIsUpload && (
                <div style={cardStyle}>
                  <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                    <div style={css('font-size:14px;font-weight:500')}>Uploaded data</div>
                    {editLink(() => patch({ editingSection: s.editingSection === 'upload' ? null : 'upload' }))}
                  </div>
                  <div style={css('font-size:14px;margin-top:4px')}>{csvSummaryLabel}</div>
                  {s.importedFields.length > 0 && (
                    <div style={css('margin-top:var(--invoicecloud-spacing-s)')}>
                      {s.importedFields.map((im, i) => (
                        <div key={i} style={css('display:flex;justify-content:space-between;font-size:13px;padding:2px 0')}>
                          <span style={css('color:var(--invoicecloud-utility-neutral-70)')}>{im.label}</span><span style={css('font-weight:500')}>{im.value}</span>
                        </div>
                      ))}
                    </div>
                  )}
                  {s.editingSection === 'upload' && (
                    <div style={css('margin-top:var(--invoicecloud-spacing-s)')}>
                      <input type="file" accept=".csv,.xlsx,.xls,.json,.yaml,.yml,.pdf" onChange={onCsvFile} style={css('font-size:13px')} />
                    </div>
                  )}
                </div>
              )}

              {setupPathIsManual && chatReviewRows.slice(0, 1).map((cr, i) => (
                <div key={i} style={cardStyle}>
                  <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                    <div style={css('font-size:14px;font-weight:500')}>{cr.question}</div>
                    <button type="button" onClick={cr.onEdit} style={css('background:none;border:none;color:var(--invoicecloud-primary);font-size:13px;font-weight:700;cursor:pointer;flex:none;margin-left:var(--invoicecloud-spacing-s)')}>Edit</button>
                  </div>
                  <div style={css('font-size:14px;margin-top:4px')}>{cr.answer}</div>
                  <div style={css('display:flex;gap:6px;margin-top:6px;font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}><img src={asset('assets/icons/Magic.svg')} alt="" style={css('width:14px;height:14px;flex:none')} />{cr.rationale}</div>
                  {cr.editing && (
                    <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-top:var(--invoicecloud-spacing-s)')}>
                      <input type="text" value={s.chatDraft} onChange={setChatDraft} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); saveChatAnswerEdit(); } }} style={css('flex:1;padding:8px 12px;border-radius:8px;border:1px solid var(--invoicecloud-surface-default-border);font-size:13px')} />
                      <button type="button" onClick={saveChatAnswerEdit} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:8px 16px;font-size:13px;font-weight:700;cursor:pointer')}>Save</button>
                    </div>
                  )}
                </div>
              ))}
            </div>

            <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m)')}>
              <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-s)')}>
                <h3 style={css('font-size:16px')}>Brand Details</h3>
                {editLink(() => openReviewSection('brand'))}
              </div>
              <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                {showUploadedLogo && <img src={s.logoDataUrl ?? ''} alt="" style={css('width:52px;height:52px;border-radius:12px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border)')} />}
                {showFetchedLogo && <img src={logoFetchUrl} alt="" style={css('width:52px;height:52px;border-radius:12px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border);background:#fff')} />}
                {showInitialsLogo && <div style={css(`width:52px;height:52px;border-radius:12px;display:flex;align-items:center;justify-content:center;color:#fff;font-weight:700;font-size:18px;background:${brand.primary}`)}>{brand.initials}</div>}
                <div>
                  <div style={css('font-weight:500')}>{s.bizName}</div>
                  <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70)')}>Suggested font: {brand.font}</div>
                </div>
              </div>
              <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                {swatches.map((hex, i) => <div key={i} title={hex} style={css(`width:36px;height:36px;border-radius:8px;border:1px solid rgba(0,0,0,.08);background:${hex}`)}></div>)}
              </div>
              <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-50);border-top:1px solid var(--invoicecloud-surface-default-border);padding-top:var(--invoicecloud-spacing-xs)')}>{brandSourceLabel}</div>
              {s.reviewEditingSection === 'brand' && (
                <div style={css('margin-top:var(--invoicecloud-spacing-m);border-top:1px solid var(--invoicecloud-surface-default-border);padding-top:var(--invoicecloud-spacing-m)')}>
                  <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Website</label>
                  <input type="text" value={s.website} onChange={setWebsite} placeholder="www.yourbusiness.com" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')} />
                  <label style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-xs);font-size:14px;color:var(--invoicecloud-utility-neutral-70);cursor:pointer;margin-bottom:var(--invoicecloud-spacing-m)')}>
                    <input type="checkbox" checked={s.skipWebsite} onChange={toggleSkipWebsite} /> I don't have a website - use smart defaults
                  </label>
                  {s.skipWebsite && (
                    <>
                      <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Upload your logo</label>
                      {showUploadedLogo ? (
                        <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-m)')}>
                          <img src={s.logoDataUrl ?? ''} alt="Uploaded logo" style={css('width:44px;height:44px;border-radius:10px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border)')} />
                          <button type="button" onClick={clearLogo} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:8px 14px;font-size:13px;cursor:pointer')}>Remove</button>
                        </div>
                      ) : (
                        <input type="file" accept="image/*" onChange={setLogoFile} style={css('font-size:13px;margin-bottom:var(--invoicecloud-spacing-m);display:block')} />
                      )}
                      <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Brand colors</label>
                      <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                        <button type="button" onClick={() => setColorChoice('auto')} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.colorChoice === 'auto')};border:1.5px solid ${selBorder(s.colorChoice === 'auto')}`)}>Pick for Me</button>
                        <button type="button" onClick={() => setColorChoice('custom')} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.colorChoice === 'custom')};border:1.5px solid ${selBorder(s.colorChoice === 'custom')}`)}>Choose my own colors</button>
                      </div>
                      {s.colorChoice === 'custom' && (
                        <div style={css('display:flex;gap:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-s)')}>
                          <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Primary<br /><input type="color" value={s.customPrimary} onChange={setCustomPrimary} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                          <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Secondary<br /><input type="color" value={s.customSecondary} onChange={setCustomSecondary} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                          <label style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>Accent<br /><input type="color" value={s.customAccent} onChange={setCustomAccent} style={css('width:56px;height:32px;padding:0;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border)')} /></label>
                        </div>
                      )}
                      <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Font</label>
                      <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                        <button type="button" onClick={() => setFontChoice('auto')} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.fontChoice === 'auto')};border:1.5px solid ${selBorder(s.fontChoice === 'auto')}`)}>Pick for Me</button>
                        <button type="button" onClick={() => setFontChoice(GOOGLE_FONTS[0])} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.fontChoice !== 'auto')};border:1.5px solid ${selBorder(s.fontChoice !== 'auto')}`)}>Choose a Google Font</button>
                      </div>
                      {s.fontChoice !== 'auto' && (
                        <select value={s.fontChoice} onChange={setFontChoice} style={css('padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px;margin-bottom:var(--invoicecloud-spacing-s)')}>
                          {GOOGLE_FONTS.map((gf) => <option key={gf} value={gf}>{gf}</option>)}
                        </select>
                      )}
                    </>
                  )}
                  <div><button type="button" ref={saveBtnRef} onClick={saveBrandSection} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 20px;font-size:14px;font-weight:700;cursor:pointer;margin-top:var(--invoicecloud-spacing-s)')}>Save</button></div>
                </div>
              )}
            </div>

            <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m)')}>
              <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Compliance</h3>
              <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s)')}>
                {complianceByState.map((cs) => (
                  <div key={cs.state} style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;overflow:hidden')}>
                    <button type="button" onClick={cs.onToggle} style={css('width:100%;display:flex;justify-content:space-between;align-items:center;padding:var(--invoicecloud-spacing-m);background:none;border:none;cursor:pointer;text-align:left')}>
                      <h3 style={css('font-size:16px')}>{cs.state}</h3>
                      <img src={asset('assets/icons/ChevronDown.svg')} alt="" style={css(`width:16px;height:16px;transform:${cs.expanded ? 'rotate(180deg)' : 'rotate(0deg)'};transition:transform .2s`)} />
                    </button>
                    {cs.expanded && (
                      <div style={css('padding:0 var(--invoicecloud-spacing-m) var(--invoicecloud-spacing-m)')}>
                        <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                          {cs.categories.map((cat, i) => (
                            <div key={i} style={css('border-bottom:1px solid var(--invoicecloud-surface-default-border);padding-bottom:var(--invoicecloud-spacing-s)')}>
                              <div style={css('font-size:13px;font-weight:500;margin-bottom:2px')}>{cat.label}</div>
                              <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70)')}>{cat.text}</div>
                            </div>
                          ))}
                        </div>
                        <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-50)')}>Simulated for this demo - not legal advice.</div>
                      </div>
                    )}
                  </div>
                ))}
              </div>
              <div style={css('border-top:1px solid var(--invoicecloud-surface-default-border);margin-top:var(--invoicecloud-spacing-m);padding-top:var(--invoicecloud-spacing-m)')}>
                <h4 style={css('font-size:14px;font-weight:500;margin-bottom:4px')}>Documentation</h4>
                <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Additional compliance documents</label>
                <p style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-s)')}>Add any municipal ordinances, riders, or internal policies that should also apply.</p>
                <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                  <input type="text" value={s.newDocName} onChange={setNewDocName} placeholder="e.g. County late-fee ordinance" style={css('flex:1;padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px')} />
                  <button type="button" onClick={addDocument} style={css('background:none;border:1px solid var(--invoicecloud-primary);color:var(--invoicecloud-primary);border-radius:8px;padding:10px 18px;font-size:13px;font-weight:700;cursor:pointer')}>+ Add</button>
                </div>
                <div style={css('display:flex;flex-wrap:wrap;gap:var(--invoicecloud-spacing-xs)')}>
                  {s.docs.map((doc, i) => (
                    <span key={i} style={css('display:flex;align-items:center;gap:6px;background:var(--invoicecloud-slate-10);border-radius:6px;padding:6px 10px;font-size:13px')}>
                      {doc}
                      <button type="button" onClick={() => removeDocument(i)} aria-label="Remove" style={css('background:none;border:none;cursor:pointer;padding:0;display:flex')}><img src={asset('assets/icons/MenuClose.svg')} alt="" style={css('width:10px;height:10px')} /></button>
                    </span>
                  ))}
                </div>
              </div>
            </div>

          </div>

          <div style={css('width:100%;max-width:900px;background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m);margin-top:var(--invoicecloud-spacing-m)')}>
            <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Customer Payment Experience &amp; Fees</h3>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Guest checkout</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'guest' ? null : 'guest' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{guestCheckoutAllowedLabel}</div>
              {rationaleRow(s.aiRationale.guestCheckoutAllowed)}
              {s.editingSection === 'guest' && yesNo(() => patch({ guestCheckoutAllowed: true, editingSection: null }), () => patch({ guestCheckoutAllowed: false, editingSection: null }), s.guestCheckoutAllowed)}
            </div>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>AutoPay</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'autopay' ? null : 'autopay' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>Offered: {offerAutopayLabel} - Enroll during payment: {enrollDuringPaymentLabel}</div>
              {rationaleRow(s.aiRationale.offerAutopay)}
              {s.editingSection === 'autopay' && (
                <>
                  <div style={css('font-size:13px;font-weight:500;margin-top:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-xs)')}>Offer AutoPay?</div>
                  {yesNo(() => patch({ offerAutopay: true }), () => patch({ offerAutopay: false, editingSection: null }), s.offerAutopay, false)}
                  {s.offerAutopay === true && (
                    <>
                      <div style={css('font-size:13px;font-weight:500;margin-top:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-xs)')}>Enroll during payment?</div>
                      {yesNo(() => patch({ enrollDuringPayment: true, editingSection: null }), () => patch({ enrollDuringPayment: false, editingSection: null }), s.enrollDuringPayment, false)}
                      {isFlorida && <div style={css('background:var(--invoicecloud-intent-warning-background);border:1px solid var(--invoicecloud-intent-warning-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);font-size:13px;color:var(--invoicecloud-intent-warning);margin-top:var(--invoicecloud-spacing-s)')}>Florida requires AutoPay enrollees to pay via bank account only - card enrollment will be restricted.</div>}
                    </>
                  )}
                </>
              )}
            </div>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Paperless billing</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'paperless' ? null : 'paperless' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{offerPaperlessLabel}</div>
              {rationaleRow(s.aiRationale.offerPaperless)}
              {s.editingSection === 'paperless' && yesNo(() => patch({ offerPaperless: true, editingSection: null }), () => patch({ offerPaperless: false, editingSection: null }), s.offerPaperless)}
            </div>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Payment reminders</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'reminders' ? null : 'reminders' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{reminderChannelLabel}</div>
              {rationaleRow(s.aiRationale.reminderChannel)}
              {s.editingSection === 'reminders' && (
                <div style={css('display:flex;flex-wrap:wrap;gap:var(--invoicecloud-spacing-xs);margin-top:var(--invoicecloud-spacing-s)')}>
                  {reminderOptions.map((r) => <button key={r.id} type="button" onClick={r.onSelect} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;cursor:pointer;background:${selBg(r.selected)};border:1.5px solid ${selBorder(r.selected)}`)}>{r.label}</button>)}
                </div>
              )}
            </div>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Accepted payment methods</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'methods' ? null : 'methods' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{acceptedMethodsLabel}</div>
              {rationaleRow(s.aiRationale.acceptedMethods)}
              {s.editingSection === 'methods' && (
                <>
                  <div style={css('display:flex;flex-wrap:wrap;gap:var(--invoicecloud-spacing-xs);margin-top:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)')}>
                    {methodOptions.map((m) => <button key={m.id} type="button" onClick={m.onToggle} style={css(`padding:10px 18px;border-radius:8px;font-size:13px;cursor:pointer;background:${selBg(m.selected)};border:1.5px solid ${selBorder(m.selected)}`)}>{m.label}</button>)}
                  </div>
                  <button type="button" onClick={() => patch({ editingSection: null })} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:8px 16px;font-size:13px;font-weight:700;cursor:pointer')}>Done</button>
                </>
              )}
            </div>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Self-service: billing history</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'history' ? null : 'history' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{selfServiceHistoryLabel}</div>
              {rationaleRow(s.aiRationale.selfServiceHistory)}
              {s.editingSection === 'history' && yesNo(() => patch({ selfServiceHistory: true, editingSection: null }), () => patch({ selfServiceHistory: false, editingSection: null }), s.selfServiceHistory)}
            </div>

            <div style={cardStyle}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Self-service: account updates</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'update' ? null : 'update' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{selfServiceUpdateLabel}</div>
              {rationaleRow(s.aiRationale.selfServiceUpdate)}
              {s.editingSection === 'update' && yesNo(() => patch({ selfServiceUpdate: true, editingSection: null }), () => patch({ selfServiceUpdate: false, editingSection: null }), s.selfServiceUpdate)}
            </div>

            <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-s)')}>
              <div style={css('display:flex;justify-content:space-between;align-items:flex-start')}>
                <div style={css('font-size:14px;font-weight:500')}>Payment processing fees</div>
                {editLink(() => patch({ editingSection: s.editingSection === 'fees' ? null : 'fees' }))}
              </div>
              <div style={css('font-size:14px;margin-top:4px')}>{feeHandlingLabel}</div>
              {rationaleRow(s.aiRationale.feeHandling)}
              {s.editingSection === 'fees' && (
                <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-xs);margin-top:var(--invoicecloud-spacing-s)')}>
                  {feeOptions.map((f) => <button key={f.id} type="button" onClick={f.onSelect} style={css(`text-align:left;padding:var(--invoicecloud-spacing-s);border-radius:10px;font-size:14px;cursor:pointer;background:${selBg(f.selected)};border:1.5px solid ${selBorder(f.selected)}`)}>{f.label}</button>)}
                </div>
              )}
            </div>
          </div>

          <div style={css('display:flex;gap:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-l)')}>
            <button type="button" onClick={reviewCompletedResearch} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:14px 24px;font-size:15px;cursor:pointer;color:var(--invoicecloud-utility-neutral-80)')}>&larr; Agent activity</button>
            <button type="button" onClick={redoWizard} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:14px 24px;font-size:15px;cursor:pointer;color:var(--invoicecloud-utility-neutral-80)')}>Redo</button>
            <button type="button" onClick={confirmPreview} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:10px;padding:14px 32px;font-size:16px;font-weight:700;cursor:pointer')}>Preview My Payment Site &rarr;</button>
          </div>
        </div>
      )}
      {/* ================= PREVIEW ================= */}
      {s.screen === 'preview' && (
        <div style={css('min-height:100vh;background:var(--invoicecloud-utility-neutral-10);display:flex;flex-direction:column;align-items:center;padding:var(--invoicecloud-spacing-l)')}>
          <div style={css(`width:100%;max-width:${previewShellMaxWidth};display:flex;align-items:center;justify-content:space-between;margin-bottom:var(--invoicecloud-spacing-s)`)}>
            <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70)')}>You're viewing this as one of <strong>{s.bizName}</strong>'s payers</div>
            <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs)')}>
              <button type="button" onClick={payerRestart} aria-label="Restart preview" title="Restart preview" style={css('background:#fff;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;width:40px;height:40px;display:flex;align-items:center;justify-content:center;cursor:pointer')}>
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="var(--invoicecloud-utility-neutral-80)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 12a9 9 0 1 0 3-6.7"></path><path d="M3 4v5h5"></path></svg>
              </button>
              <div style={css('display:flex;background:var(--invoicecloud-slate-10);border-radius:10px;padding:3px')}>
                <button type="button" onClick={() => setPreviewDevice('desktop')} style={css(`padding:8px 14px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.previewDevice === 'desktop')};border:1.5px solid ${selBorder(s.previewDevice === 'desktop')}`)}>Desktop</button>
                <button type="button" onClick={() => setPreviewDevice('mobile')} style={css(`padding:8px 14px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(s.previewDevice === 'mobile')};border:1.5px solid ${selBorder(s.previewDevice === 'mobile')}`)}>Mobile</button>
              </div>
              <button type="button" onClick={backToResults} style={css('background:var(--invoicecloud-utility-neutral-90);color:#fff;border:none;border-radius:10px;padding:10px 18px;font-size:14px;font-weight:700;cursor:pointer')}>&larr; Back to builder</button>
            </div>
          </div>

          <section style={css(`width:100%;max-width:${previewMaxWidth};background:#fff;border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-s);box-shadow:var(--invoicecloud-elevation-1)`)}>
            <h3 style={css('font-size:16px;margin-bottom:4px')}>Billing policy generated by the agents</h3>
            <p style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:12px')}>The agents used your selected billing categories and conservative defaults. These values never block publishing.</p>
            {s.backendSession?.discovery_progress && (
              <div style={css('margin-bottom:12px;padding:12px;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;background:var(--invoicecloud-utility-neutral-10)')}>
                <div style={css('display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:8px')}>
                  <strong>Billing policy</strong>
                  <span style={css(`font-size:12px;font-weight:700;color:${s.backendSession.discovery_progress.is_complete ? '#197d00' : 'var(--invoicecloud-primary)'}`)}>
                    {s.backendSession.discovery_progress.completed} of {s.backendSession.discovery_progress.total} policy details set
                  </span>
                </div>
                {!!s.backendSession.billing_profile?.assumptions?.length && (
                  <div role="note" style={css('padding:10px;margin-bottom:10px;border-radius:8px;background:#fff7e6;border:1px solid #e8b65a;font-size:12px')}>
                    <strong>Agent assumptions applied</strong>
                    <ul style={css('margin:6px 0 0;padding-left:18px')}>
                      {s.backendSession.billing_profile.assumptions.map(assumption => <li key={assumption.question_id}>{assumption.description}</li>)}
                    </ul>
                    <div style={css('margin-top:6px')}>These values are applied to the generated experience and can be changed in the builder later.</div>
                  </div>
                )}
                {(s.backendSession.billing_profile?.categories.length ?? 0) === 0 ? (
                  <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70)')}>No billing categories confirmed yet.</div>
                ) : (
                  <div style={css('display:grid;gap:8px')}>
                    {s.backendSession.billing_profile?.categories.map(category => (
                      <div key={category.id} style={css('padding:9px;background:#fff;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px')}>
                        <div style={css('display:flex;justify-content:space-between;gap:8px;align-items:center')}>
                          <strong style={css('font-size:13px')}>{category.display_name}</strong>
                        </div>
                        <div style={css('display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin-top:6px;font-size:12px')}>
                          <span><b>Cadence:</b> {category.cadence?.kind.replace('_', ' ') ?? 'Needs input'}</span>
                          <span><b>Late/state:</b> {category.state_rules?.[0]?.description ?? 'Agent will apply a safe default'}</span>
                          <span><b>Terms:</b> {category.payment_terms?.mode === 'installments_allowed' ? `Installments${category.payment_terms.maximum_installments ? ` (max ${category.payment_terms.maximum_installments})` : ''}` : category.payment_terms?.mode === 'pay_in_full' ? 'Pay in full' : 'Agent will apply a safe default'}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
            {false && <>
            <form onSubmit={submitPreviewChange} style={css('display:flex;gap:8px')}>
              <input ref={previewChatInputRef} value={s.previewChatInput} onChange={event => patch({ previewChatInput: event.target.value })} disabled={s.previewChatBusy || !!s.previewProposal} placeholder={s.backendSession?.current_question ? 'Answer the required question…' : 'e.g. Make the heading friendlier and change the button to Pay later'} style={css('flex:1;padding:11px 12px;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;font-size:14px')} />
              <button type="submit" disabled={s.previewChatBusy || !!s.previewProposal || !s.previewChatInput.trim()} style={css('border:0;border-radius:8px;padding:10px 16px;background:var(--invoicecloud-primary);color:#fff;font-weight:700;cursor:pointer')}>{s.previewChatBusy ? 'Working...' : billingInterviewPending(s.backendSession) ? 'Send answer' : 'Propose change'}</button>
            </form>
            {s.previewChatError && <div role="alert" style={css('margin-top:10px;padding:10px;border-radius:8px;background:var(--invoicecloud-intent-error-background);color:var(--invoicecloud-intent-error)')}>{s.previewChatError}</div>}
            {s.previewGenerationMode && s.previewGenerationMode !== 'azure_ai' && (
              <div role="status" style={css('margin-top:10px;display:inline-flex;align-items:center;gap:6px;padding:4px 10px;border-radius:999px;background:#f6e6c8;color:#7a4b00;font-size:12px;font-weight:700')}>
                Offline mode — generated by the deterministic designer (Foundry model unavailable)
              </div>
            )}
            {s.previewChatReply && <p aria-live="polite" style={css('margin:10px 0 0;font-size:13px')}>{s.previewChatReply}</p>}
            {s.previewChatBusy && <AgentActivityPanel activity={s.agentActivity} connection={s.activityConnection} />}
            {s.previewProposal && (
              <div style={css('margin-top:12px;padding:12px;border:1px solid var(--invoicecloud-primary);border-radius:10px;background:var(--invoicecloud-primary-tint)')}>
                <strong>Proposed revision {s.previewProposal!.revision}</strong>
                {(() => {
                  const changes = proposedChanges(s.backendDraft?.definition, s.previewProposal!.definition);
                  return changes.length ? (
                    <ul style={css('margin:8px 0 10px;padding-left:20px;font-size:13px')}>
                      {changes.map(change => <li key={change.field}>{change.field}: {change.detail}</li>)}
                    </ul>
                  ) : (
                    <p style={css('margin:8px 0 10px;font-size:13px')}>No changes the preview can apply were detected in your request. The offline designer supports primary color and heading/button-label edits — try rephrasing, e.g. "change the primary color to purple".</p>
                  );
                })()}
                {(s.previewProposal!.findings ?? []).map(finding => <div key={finding.code} style={css('font-size:12px;color:#b54708')}>{finding.message}</div>)}
                <div style={css('display:flex;gap:8px;margin-top:10px')}>
                  <button type="button" onClick={acceptPreviewChange} style={css('border:0;border-radius:8px;padding:9px 14px;background:#197d00;color:#fff;font-weight:700;cursor:pointer')}>Accept and update preview</button>
                  <button type="button" onClick={() => void rejectPreviewChange()} style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:9px 14px;background:#fff;cursor:pointer')}>Discard</button>
                </div>
              </div>
            )}
            </>}
          </section>

          {s.payerStep === 0 && (
            <>
              <h3 style={css(`width:100%;max-width:${previewShellMaxWidth};font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)`)}>Preview Scenarios</h3>
              <div style={css(`width:100%;max-width:${previewShellMaxWidth};display:grid;grid-template-columns:${scenarioGridCols};gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s)`)}>
                {scenarioCards.map((sc) => <button key={sc.id} type="button" onClick={sc.onSelect} style={css(`padding:var(--invoicecloud-spacing-s);border-radius:10px;font-size:13px;font-weight:500;cursor:pointer;text-align:center;background:${selBg(sc.selected)};border:1.5px solid ${selBorder(sc.selected)}`)}>{sc.label}</button>)}
              </div>
              {s.previewScenario === 'complex' && (
                <div style={css(`width:100%;max-width:${previewShellMaxWidth};background:#fff;border-radius:14px;padding:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-s);box-shadow:var(--invoicecloud-elevation-2)`)}>
                  <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Describe a scenario</label>
                  <textarea value={s.complexScenarioText} onChange={setComplexScenarioText} placeholder="e.g. show me what a delinquent payment will look like" style={css('width:100%;min-height:70px;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:14px;font-family:var(--invoicecloud-font-family-primary);margin-bottom:var(--invoicecloud-spacing-s)')}></textarea>
                  <button type="button" onClick={previewScenarioClick} disabled={s.scenarioLoading} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 20px;font-size:14px;font-weight:700;cursor:pointer')}>Preview Scenario</button>
                </div>
              )}
            </>
          )}

          <div data-preview-root style={css(`width:100%;max-width:${previewMaxWidth};background:#fff;border-radius:14px;overflow:hidden;box-shadow:var(--invoicecloud-elevation-3);font-family:'${safeFontName(brand.font)}'`)}>
            <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-xs);padding:var(--invoicecloud-spacing-s);background:var(--invoicecloud-utility-neutral-05);border-bottom:1px solid var(--invoicecloud-surface-default-border)')}>
              <span style={css('width:10px;height:10px;border-radius:50%;background:#e35b4f')}></span>
              <span style={css('width:10px;height:10px;border-radius:50%;background:#e8c34a')}></span>
              <span style={css('width:10px;height:10px;border-radius:50%;background:#5db462')}></span>
              <div style={css('margin-left:var(--invoicecloud-spacing-s);background:#fff;border:1px solid var(--invoicecloud-surface-default-border);border-radius:6px;padding:4px 12px;font-size:12px;color:var(--invoicecloud-utility-neutral-60);font-family:var(--invoicecloud-font-family-mono)')}>pay.{siteSlug}.com</div>
            </div>

            <div style={css('padding:var(--invoicecloud-spacing-l) var(--invoicecloud-spacing-xl)')}>
              <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-l)')}>
                {showUploadedLogo && <img src={s.logoDataUrl ?? ''} alt="" style={css('width:40px;height:40px;border-radius:10px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border)')} />}
                {showFetchedLogo && <img src={logoFetchUrl} alt="" style={css('width:40px;height:40px;border-radius:10px;object-fit:cover;border:1px solid var(--invoicecloud-surface-default-border);background:#fff')} />}
                {showInitialsLogo && <div style={css(`width:40px;height:40px;border-radius:10px;display:flex;align-items:center;justify-content:center;color:#fff;font-weight:700;background:${brand.primary}`)}>{brand.initials}</div>}
                <div style={css('font-weight:500;font-size:18px')}>{s.bizName}</div>
                <div style={css('flex:1')}></div>
                <div style={css('text-align:right')}>
                  <div style={css('font-size:14px;font-weight:500')}>Jordan Ellis</div>
                  <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);font-family:var(--invoicecloud-font-family-mono)')}>Acct ....4421</div>
                </div>
              </div>

              {s.payerStep === 0 && previewHeading && (
                <h2 style={css('font-size:22px;font-weight:700;margin-bottom:var(--invoicecloud-spacing-m)')}>{previewHeading}</h2>
              )}

              {s.payerStep === 0 && billingCategories.length > 0 && (
                <section aria-label="Billing options" style={css('margin-bottom:var(--invoicecloud-spacing-l);border:1px solid var(--invoicecloud-surface-default-border);border-radius:12px;padding:var(--invoicecloud-spacing-m)')}>
                  <div style={css('font-size:13px;font-weight:700;margin-bottom:var(--invoicecloud-spacing-s)')}>What you can pay</div>
                  <div style={css('display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:var(--invoicecloud-spacing-s)')}>
                    {billingCategories.map(category => (
                      <article key={category.id} style={css('background:var(--invoicecloud-slate-10);border-radius:10px;padding:var(--invoicecloud-spacing-s)')}>
                        <div style={css('font-size:14px;font-weight:700')}>{category.display_name}</div>
                        <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70);margin-top:3px')}>{category.cadence_label}</div>
                        <div style={css('font-size:12px;margin-top:6px')}>{paymentTermsLabel(category.payment_mode, category.maximum_installments)}</div>
                        <div style={css('font-size:11px;color:var(--invoicecloud-utility-neutral-60);margin-top:4px')}>{category.state_summary}</div>
                      </article>
                    ))}
                  </div>
                </section>
              )}

              {s.payerStep === 0 && (
                <>
                  {s.previewScenario === 'payment' && (
                    <>
                      <div style={css(`border-radius:14px;padding:var(--invoicecloud-spacing-l);color:#fff;margin-bottom:var(--invoicecloud-spacing-l);background:${brand.primary}`)}>
                        <div style={css('font-size:13px;opacity:.85;margin-bottom:4px')}>{`Amount due · ${heroDueText}`}</div>
                        <div style={css('font-size:36px;font-weight:700;font-family:var(--invoicecloud-font-family-mono);margin-bottom:var(--invoicecloud-spacing-s)')}>${amount.toFixed(2)}</div>
                        <button type="button" onClick={payerGoPay} style={css(`background:#fff;border:none;border-radius:10px;padding:12px 28px;font-size:15px;font-weight:700;cursor:pointer;color:${brand.primary}`)}>{primaryActionLabel}</button>
                      </div>
                      <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-s)')}>
                        <div style={css('display:flex;background:var(--invoicecloud-slate-10);border-radius:10px;padding:3px')}>
                          {statementTabs.map((t) => (
                            <button key={t.id} type="button" onClick={t.onSelect} style={css(`padding:6px 16px;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;background:${selBg(t.selected)};border:1.5px solid ${selBorder(t.selected)}`)}>{t.label} ({t.count})</button>
                          ))}
                        </div>
                        <span style={css(`font-size:11px;font-weight:700;padding:4px 10px;border-radius:4px;background:${dataBadgeBg};color:${dataBadgeColor}`)}>{dataBadgeLabel}</span>
                      </div>
                      {shownStatements.length === 0 && <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-60);padding:var(--invoicecloud-spacing-s) 0')}>No {s.statementTab} statements.</div>}
                      {shownStatements.map((st) => (
                        <div key={st.id} style={css('padding:var(--invoicecloud-spacing-s) 0;border-bottom:1px solid var(--invoicecloud-surface-default-border)')}>
                          <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                            <div>
                              {st.type && <span style={css(`display:inline-block;font-size:11px;font-weight:700;letter-spacing:.03em;text-transform:uppercase;padding:2px 8px;border-radius:999px;margin-bottom:4px;background:${brand.accent};color:${brand.primary}`)}>{st.type}</span>}
                              <button type="button" onClick={st.onClick} style={css(`display:block;background:none;border:none;padding:0;cursor:pointer;text-align:left;font-weight:500;font-size:14px;text-decoration:underline;color:${brand.primary}`)}>{st.label}</button>
                              <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>{st.date}</div>
                            </div>
                            <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s)')}>
                              {st.statusColor && <span aria-hidden="true" style={css(`width:10px;height:10px;border-radius:50%;background:${st.statusColor === 'yellow' ? '#f5c542' : '#3ecf8e'}`)}></span>}
                              <span style={css(`font-size:12px;font-weight:700;padding:4px 10px;border-radius:4px;background:${st.badgeBg};color:${st.badgeColor}`)}>{st.status}</span>
                              <button type="button" onClick={st.onClick} aria-label="View invoice" style={css('background:none;border:none;cursor:pointer;padding:0;display:flex')}><img src={asset('assets/icons/ChevronRight.svg')} alt="" style={css('width:14px;height:14px')} /></button>
                            </div>
                          </div>
                          {st.note && <div style={css(`margin-top:6px;font-size:12px;line-height:1.4;color:var(--invoicecloud-utility-neutral-70);${st.noteEmphasis ? 'font-weight:700;color:var(--invoicecloud-utility-neutral-90)' : ''}`)}>{st.note}</div>}
                        </div>
                      ))}

                      {(s.offerAutopay || s.offerPaperless) && (
                        <div style={css('margin-top:var(--invoicecloud-spacing-l);display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s)')}>
                          {s.offerAutopay && (
                            <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:12px;padding:var(--invoicecloud-spacing-m)')}>
                              <div style={css('display:flex;justify-content:space-between;align-items:flex-start;gap:var(--invoicecloud-spacing-s)')}>
                                <div>
                                  <div style={css('font-weight:500;font-size:15px;display:flex;align-items:center;gap:6px')}><img src={asset('assets/icons/AutoPay.svg')} alt="" style={css('width:18px;height:18px')} />AutoPay</div>
                                  <div style={css(`font-size:12px;margin-top:2px;color:${s.previewAutopayEnrolled ? 'var(--invoicecloud-intent-success)' : 'var(--invoicecloud-utility-neutral-60)'}`)}>{previewAutopayStatusLabel}</div>
                                </div>
                                {s.previewAutopayEnrolled
                                  ? <button type="button" onClick={unenrollPreviewAutopay} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:8px 16px;font-size:13px;font-weight:500;cursor:pointer')}>Unenroll</button>
                                  : <button type="button" onClick={togglePreviewAutopayEnrolling} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:8px 16px;font-size:13px;font-weight:700;cursor:pointer')}>{s.previewAutopayEnrolling ? 'Cancel' : 'Enroll'}</button>}
                              </div>
                              {!s.previewAutopayEnrolled && s.previewAutopayEnrolling && (
                                <div style={css('margin-top:var(--invoicecloud-spacing-s);border-top:1px solid var(--invoicecloud-surface-default-border);padding-top:var(--invoicecloud-spacing-s)')}>
                                  <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:6px')}>Next AutoPay charge: <strong>${amount.toFixed(2)} on Aug 4, 2026</strong></div>
                                  <div style={css('font-size:13px;font-weight:500;margin-bottom:6px')}>Choose a payment method</div>
                                  <label style={css('display:flex;align-items:center;gap:8px;font-size:14px;padding:8px 0;cursor:pointer')}>
                                    <input type="radio" name="preview-autopay-source" checked={s.previewAutopaySource === 'existing'} onChange={choosePreviewAutopayExisting} /> Use card on file ····4242
                                  </label>
                                  <label style={css('display:flex;align-items:center;gap:8px;font-size:14px;padding:8px 0;cursor:pointer')}>
                                    <input type="radio" name="preview-autopay-source" checked={s.previewAutopaySource === 'new'} onChange={choosePreviewAutopayNew} /> Add a new payment method
                                  </label>
                                  {s.previewAutopaySource === 'new' && (
                                    <div style={css('margin:6px 0 var(--invoicecloud-spacing-s)')}>
                                      {isFlorida && <div style={css('background:var(--invoicecloud-intent-info-background);border:1px solid var(--invoicecloud-intent-info-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s);font-size:12px;color:var(--invoicecloud-intent-info)')}>Florida requires AutoPay to be funded by a bank account.</div>}
                                      <div style={css('display:flex;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-s)')}>
                                        <button type="button" onClick={previewAutopaySetCard} disabled={isFlorida} style={css(`flex:1;padding:8px;border-radius:8px;font-size:13px;cursor:${isFlorida ? 'not-allowed' : 'pointer'};background:${selBg(!isFlorida && s.previewAutopayMethodType === 'card')};border:1.5px solid ${selBorder(!isFlorida && s.previewAutopayMethodType === 'card')};opacity:${isFlorida ? 0.5 : 1}`)}>Card</button>
                                        <button type="button" onClick={previewAutopaySetBank} style={css(`flex:1;padding:8px;border-radius:8px;font-size:13px;cursor:pointer;background:${selBg(isFlorida || s.previewAutopayMethodType === 'bank')};border:1.5px solid ${selBorder(isFlorida || s.previewAutopayMethodType === 'bank')}`)}>Bank account</button>
                                      </div>
                                      {(isFlorida || s.previewAutopayMethodType === 'bank')
                                        ? <><input type="text" placeholder="Routing number" style={css('width:100%;padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono);margin-bottom:var(--invoicecloud-spacing-xs)')} /><input type="text" placeholder="Account number" style={css('width:100%;padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono)')} /></>
                                        : <input type="text" placeholder="Card number" style={css('width:100%;padding:10px 12px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono)')} />}
                                    </div>
                                  )}
                                  <button type="button" onClick={confirmPreviewAutopayEnroll} style={css('margin-top:6px;background:var(--invoicecloud-intent-success-hover);color:#fff;border:none;border-radius:8px;padding:10px 20px;font-size:13px;font-weight:700;cursor:pointer')}>Confirm enrollment</button>
                                </div>
                              )}
                            </div>
                          )}
                          {s.offerPaperless && (
                            <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:12px;padding:var(--invoicecloud-spacing-m);display:flex;justify-content:space-between;align-items:center;gap:var(--invoicecloud-spacing-s)')}>
                              <div>
                                <div style={css('font-weight:500;font-size:15px;display:flex;align-items:center;gap:6px')}><img src={asset('assets/icons/Paperless.svg')} alt="" style={css('width:18px;height:18px')} />Paperless billing</div>
                                <div style={css(`font-size:12px;margin-top:2px;color:${s.previewPaperlessEnrolled ? 'var(--invoicecloud-intent-success)' : 'var(--invoicecloud-utility-neutral-60)'}`)}>{previewPaperlessStatusLabel}</div>
                              </div>
                              <button type="button" role="switch" aria-checked={s.previewPaperlessEnrolled} onClick={togglePreviewPaperless} aria-label="Toggle paperless billing" style={css(`width:46px;height:26px;border-radius:13px;border:none;cursor:pointer;position:relative;transition:background .2s;background:${s.previewPaperlessEnrolled ? 'var(--invoicecloud-intent-success)' : 'var(--invoicecloud-slate-30)'}`)}>
                                <span style={css(`position:absolute;top:3px;left:${s.previewPaperlessEnrolled ? '23px' : '3px'};width:20px;height:20px;border-radius:50%;background:#fff;transition:left .2s`)}></span>
                              </button>
                            </div>
                          )}
                        </div>
                      )}
                    </>
                  )}

                  {s.previewScenario === 'history' && (
                    <>
                      <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-s)')}>
                        <h3 style={css('font-size:16px')}>Account history</h3>
                        <span style={css(`font-size:11px;font-weight:700;padding:4px 10px;border-radius:4px;background:${dataBadgeBg};color:${dataBadgeColor}`)}>{dataBadgeLabel}</span>
                      </div>
                      {historyRows.map((h, i) => (
                        <div key={i} style={css('display:flex;justify-content:space-between;align-items:center;padding:var(--invoicecloud-spacing-s) 0;border-bottom:1px solid var(--invoicecloud-surface-default-border)')}>
                          <div><div style={css('font-weight:500;font-size:14px')}>{h.desc}</div><div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60)')}>{h.date}</div></div>
                          <div style={css('text-align:right')}><div style={css('font-family:var(--invoicecloud-font-family-mono);font-size:14px')}>{h.amount}</div><div style={css('font-size:12px;color:var(--invoicecloud-intent-success)')}>{h.status}</div></div>
                        </div>
                      ))}
                    </>
                  )}

                  {s.previewScenario === 'communication' && (
                    <>
                      <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-s)')}>
                        <h3 style={css('font-size:16px')}>Communication preferences</h3>
                        <span style={css(`font-size:11px;font-weight:700;padding:4px 10px;border-radius:4px;background:${dataBadgeBg};color:${dataBadgeColor}`)}>{dataBadgeLabel}</span>
                      </div>
                      <div style={css('display:flex;flex-direction:column;gap:var(--invoicecloud-spacing-s)')}>
                        <div style={css('display:flex;justify-content:space-between;padding:var(--invoicecloud-spacing-s);border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px')}><span style={css('font-size:14px')}>Payment reminders</span><span style={css('font-size:14px;font-weight:500')}>{reminderChannelLabel}</span></div>
                        <div style={css('display:flex;justify-content:space-between;padding:var(--invoicecloud-spacing-s);border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px')}><span style={css('font-size:14px')}>Paperless billing</span><span style={css('font-size:14px;font-weight:500')}>{offerPaperlessLabel}</span></div>
                        <div style={css('display:flex;justify-content:space-between;padding:var(--invoicecloud-spacing-s);border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px')}><span style={css('font-size:14px')}>AutoPay</span><span style={css('font-size:14px;font-weight:500')}>{offerAutopayLabel}</span></div>
                      </div>
                    </>
                  )}

                  {s.previewScenario === 'complex' && (
                    <>
                      <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Complex scenario</h3>
                      {s.scenarioLoading && (
                        <div style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-s);color:var(--invoicecloud-utility-neutral-60);font-size:14px')}>
                          <img src={asset('assets/icons/Spinner.svg')} alt="" style={css('width:18px;height:18px;animation:spin 1s linear infinite')} />Generating scenario...
                        </div>
                      )}
                      {s.complexScenarioResult && (
                        <div style={css(`background:var(--invoicecloud-intent-${s.complexScenarioResult.intent}-background);border-radius:10px;padding:var(--invoicecloud-spacing-m)`)}>
                          <div style={css(`font-weight:500;margin-bottom:var(--invoicecloud-spacing-xs);color:var(--invoicecloud-intent-${s.complexScenarioResult.intent})`)}>{s.complexScenarioResult.title}</div>
                          {s.complexScenarioResult.lines.map((ln, i) => <div key={i} style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-80);margin-bottom:4px')}>{ln}</div>)}
                        </div>
                      )}
                    </>
                  )}
                </>
              )}

              {viewingStatement && (
                <div style={css('position:fixed;inset:0;background:rgba(28,28,28,.5);display:flex;align-items:center;justify-content:center;z-index:50;padding:var(--invoicecloud-spacing-l)')}>
                  <div style={css('background:#fff;border-radius:14px;width:100%;max-width:560px;max-height:85vh;overflow-y:auto;box-shadow:var(--invoicecloud-elevation-3)')}>
                    <div style={css('position:sticky;top:0;display:flex;justify-content:flex-end;padding:var(--invoicecloud-spacing-s) var(--invoicecloud-spacing-s) 0')}>
                      <button type="button" onClick={closeStatement} aria-label="Close" style={css('background:none;border:none;cursor:pointer;padding:6px')}><img src={asset('assets/icons/MenuClose.svg')} alt="" style={css('width:14px;height:14px')} /></button>
                    </div>
                    <div style={css('border-radius:14px;overflow:hidden;margin-top:-8px')}>
                      <div style={css(`padding:var(--invoicecloud-spacing-l);background:${brand.primary};color:#fff;display:flex;justify-content:space-between;align-items:flex-start`)}>
                        <div>
                          <div style={css('font-size:12px;text-transform:uppercase;letter-spacing:.06em;opacity:.8;margin-bottom:4px')}>{viewingStatement.type ? `${viewingStatement.docLabel} · ${viewingStatement.type}` : viewingStatement.docLabel}</div>
                          <div style={css('font-size:22px;font-weight:700')}>{viewingStatement.numberDisplay}</div>
                        </div>
                        <span style={css(`font-size:12px;font-weight:700;padding:6px 12px;border-radius:4px;background:rgba(255,255,255,.9);color:${brand.primary}`)}>{viewingStatement.status}</span>
                      </div>
                      <div style={css('padding:var(--invoicecloud-spacing-l)')}>
                        <div style={css('display:grid;grid-template-columns:1fr 1fr;gap:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-l);font-size:14px')}>
                          <div><div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-bottom:2px')}>{viewingStatement.personLabel}</div><div style={css('font-weight:500')}>Jordan Ellis</div></div>
                          <div><div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-bottom:2px')}>{viewingStatement.issuerLabel}</div><div style={css('font-weight:500')}>{s.bizName}</div></div>
                          <div><div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-bottom:2px')}>{viewingStatement.periodLabel}</div><div style={css('font-weight:500;font-family:var(--invoicecloud-font-family-mono)')}>{viewingStatement.period}</div></div>
                          <div><div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-bottom:2px')}>{viewingStatement.due}</div><div style={css('font-weight:500')}>{viewingStatement.date}</div></div>
                        </div>
                        {viewingStatement.note && <div style={css(`border-radius:10px;padding:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-m);font-size:13px;line-height:1.45;background:${viewingStatement.statusColor === 'yellow' ? 'var(--invoicecloud-intent-warning-background)' : 'var(--invoicecloud-slate-10)'};${viewingStatement.noteEmphasis ? 'font-weight:700' : ''}`)}>{viewingStatement.note}</div>}
                        <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-m)')}>
                          {viewingStatement.breakdown.map((line, i) => (
                            <div key={i} style={css('display:flex;justify-content:space-between;font-size:14px;padding:8px 0;border-bottom:1px solid var(--invoicecloud-surface-default-border)')}>
                              <span>{line.label}</span><span style={css('font-family:var(--invoicecloud-font-family-mono)')}>${line.amountFormatted}</span>
                            </div>
                          ))}
                          <div style={css('display:flex;justify-content:space-between;font-weight:500;padding-top:var(--invoicecloud-spacing-s)')}>
                            <span>{viewingStatement.totalLabel}</span><span style={css('font-family:var(--invoicecloud-font-family-mono);font-size:20px')}>${viewingStatement.amountFormatted}</span>
                          </div>
                        </div>
                        <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                          <button type="button" style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:8px;padding:10px 16px;font-size:13px;cursor:pointer;display:flex;align-items:center;gap:6px')}><img src={asset('assets/icons/Download.svg')} alt="" style={css('width:14px;height:14px')} />Download PDF</button>
                          {viewingStatement.isDue && <button type="button" onClick={payFromStatement} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 20px;font-size:13px;font-weight:700;cursor:pointer')}>Pay Now</button>}
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {s.payerStep === 1 && (
                <>
                  <h3 style={css('margin-bottom:var(--invoicecloud-spacing-m)')}>Checkout</h3>
                  <div style={css('display:grid;grid-template-columns:1.5fr 1fr;gap:var(--invoicecloud-spacing-l);align-items:start')}>
                    <div>
                      <h4 style={css('font-size:15px;margin-bottom:var(--invoicecloud-spacing-s)')}>1. Payment method</h4>
                      <p style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-s)')}>No payment methods on file yet - add one to pay this bill.</p>
                      <div style={css('display:flex;flex-wrap:wrap;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-m)')}>
                        {methodTypes.map((mt) => (
                          <button key={mt.id} type="button" onClick={mt.onSelect} disabled={mt.disabled} style={css(`flex:1 1 30%;min-width:100px;display:flex;align-items:center;justify-content:center;gap:8px;padding:var(--invoicecloud-spacing-s);border-radius:10px;cursor:pointer;background:${mt.id === s.methodType ? TINT : mt.disabled ? 'var(--invoicecloud-utility-neutral-05)' : '#fff'};border:1.5px solid ${mt.id === s.methodType ? PRIMARY : BORDER};font-size:14px;font-weight:500;opacity:${mt.opacity}`)}>
                            {mt.label}
                          </button>
                        ))}
                      </div>
                      {acceptedMethodChips.length > 0 && (
                        <div style={css('display:flex;flex-wrap:wrap;gap:6px;margin-bottom:var(--invoicecloud-spacing-m)')}>
                          {acceptedMethodChips.map((c) => <span key={c.id} style={css('font-size:12px;padding:4px 10px;border-radius:6px;background:var(--invoicecloud-slate-10);color:var(--invoicecloud-utility-neutral-70)')}>Also accepts {c.label}</span>)}
                        </div>
                      )}
                      {methodTypeIsWallet && (
                        <div style={css('background:var(--invoicecloud-slate-10);border-radius:10px;padding:var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-m);font-size:13px;color:var(--invoicecloud-utility-neutral-80)')}>You'll be redirected to {methodTypeWalletLabel} to complete this payment.</div>
                      )}
                      {s.methodType === 'card' && (
                        <>
                          <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Card number</label>
                          <input type="text" inputMode="numeric" autoComplete="cc-number" value={s.payCardNumber} onChange={setPayCardNumber} placeholder="4242 4242 4242 4242" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono);margin-bottom:var(--invoicecloud-spacing-s)')} />
                          <div style={css('display:flex;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-m)')}>
                            <input type="text" inputMode="numeric" autoComplete="cc-exp" value={s.payCardExpiry} onChange={setPayCardExpiry} placeholder="MM/YY" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono)')} />
                            <input type="text" inputMode="numeric" autoComplete="cc-csc" value={s.payCardCvc} onChange={setPayCardCvc} placeholder="CVC" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono)')} />
                          </div>
                        </>
                      )}
                      {s.methodType === 'bank' && (
                        <>
                          <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Routing number</label>
                          <input type="text" inputMode="numeric" value={s.payBankRouting} onChange={setPayBankRouting} placeholder="021000021" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono);margin-bottom:var(--invoicecloud-spacing-s)')} />
                          <label style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Account number</label>
                          <input type="text" inputMode="numeric" value={s.payBankAccount} onChange={setPayBankAccount} placeholder="000123456789" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono);margin-bottom:var(--invoicecloud-spacing-m)')} />
                        </>
                      )}
                      <h4 style={css('font-size:15px;margin-bottom:var(--invoicecloud-spacing-s);margin-top:var(--invoicecloud-spacing-m)')}>2. AutoPay &amp; Paperless enrollment</h4>
                      {s.offerAutopay && s.enrollDuringPayment && (
                        <>
                          <div style={css('background:var(--invoicecloud-intent-warning-background);border:1px solid var(--invoicecloud-intent-warning-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-m);font-size:13px;color:var(--invoicecloud-intent-warning)')}>Note: signing up for AutoPay will automatically enroll you in Paperless Billing as well.</div>
                          {isFlorida && <div style={css('background:var(--invoicecloud-intent-info-background);border:1px solid var(--invoicecloud-intent-info-border);border-radius:10px;padding:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-s);font-size:13px;color:var(--invoicecloud-intent-info)')}>Florida requires AutoPay to be funded by a bank account - card enrollment isn't available.</div>}
                          <label style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-xs);font-size:14px;margin-bottom:var(--invoicecloud-spacing-xxs);cursor:pointer')}>
                            <input type="checkbox" checked={s.autopayOptIn} onChange={toggleAutopay} /> Enroll in AutoPay for future bills
                          </label>
                        </>
                      )}
                      {s.offerPaperless && (
                        <>
                          <label style={css('display:flex;align-items:center;gap:var(--invoicecloud-spacing-xs);font-size:14px;margin-bottom:var(--invoicecloud-spacing-xxs);cursor:pointer;margin-top:var(--invoicecloud-spacing-s)')}>
                            <input type="checkbox" checked={s.paperlessOptIn} onChange={togglePaperless} /> Switch to Paperless Billing
                          </label>
                          <div style={css('font-size:12px;color:var(--invoicecloud-intent-success);margin-bottom:var(--invoicecloud-spacing-m)')}>Save an estimated 6 lbs of paper a year and get your bill up to 2 days faster.</div>
                        </>
                      )}
                      <h4 style={css('font-size:15px;margin-bottom:4px;margin-top:var(--invoicecloud-spacing-m)')}>3. Optional: Create an account</h4>
                      <p style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-s)')}>Save a password to view your history and manage payments online. You can skip this and pay as a guest.</p>
                      <input type="email" value={s.accountEmail} onChange={setAccountEmail} placeholder="Email address" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);margin-bottom:var(--invoicecloud-spacing-s)')} />
                      <input type="password" value={s.accountPassword} onChange={setAccountPassword} placeholder="Create a password (optional)" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);margin-bottom:var(--invoicecloud-spacing-m)')} />
                      <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin:var(--invoicecloud-spacing-m) 0')}>By selecting the button below, you agree to the <a href={asset('legal/terms.html')} target="_blank" rel="noopener noreferrer" style={css('color:var(--invoicecloud-primary)')}>Pronto Terms and Conditions</a> and <a href={asset('legal/fee-disclosure.html')} target="_blank" rel="noopener noreferrer" style={css('color:var(--invoicecloud-primary)')}>Fee Disclosure</a>.</div>
                      {s.payError && (
                        <div role="alert" style={css('background:var(--invoicecloud-intent-error-background);border:1px solid var(--invoicecloud-intent-error-border);color:var(--invoicecloud-intent-error);border-radius:8px;padding:10px 12px;font-size:13px;margin-bottom:var(--invoicecloud-spacing-s)')}>{s.payError}</div>
                      )}
                      <div style={css('display:flex;justify-content:space-between;align-items:center')}>
                        <button type="button" onClick={payerBack} style={css('background:none;border:none;color:var(--invoicecloud-primary);font-weight:700;cursor:pointer')}>&larr; Back</button>
                        <button type="button" onClick={submitPayment} disabled={s.processing} style={css('background:var(--invoicecloud-intent-success-hover);color:#fff;border:none;border-radius:10px;padding:14px 32px;font-weight:700;cursor:pointer;font-size:15px')}>{s.processing ? 'Processing...' : 'Pay Now'}</button>
                      </div>
                    </div>

                    <div style={css('border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m);position:sticky;top:0')}>
                      <h4 style={css('font-size:15px;margin-bottom:var(--invoicecloud-spacing-s)')}>{docLabels.docLabel}</h4>
                      <div style={css('display:flex;justify-content:space-between;padding-bottom:var(--invoicecloud-spacing-xs);border-bottom:1px solid var(--invoicecloud-surface-default-border);margin-bottom:var(--invoicecloud-spacing-xs);font-size:14px')}><span>Amount due</span><span style={css('font-family:var(--invoicecloud-font-family-mono)')}>${amount.toFixed(2)}</span></div>
                      {serviceFeeApplies && (
                        <div style={css('display:flex;justify-content:space-between;padding-bottom:var(--invoicecloud-spacing-xs);border-bottom:1px solid var(--invoicecloud-surface-default-border);margin-bottom:var(--invoicecloud-spacing-xs);font-size:14px')}><span>Service fee*</span><span style={css('font-family:var(--invoicecloud-font-family-mono)')}>${serviceFee.toFixed(2)}</span></div>
                      )}
                      <div style={css('display:flex;justify-content:space-between;font-weight:500;margin-bottom:var(--invoicecloud-spacing-s)')}><span>Total</span><span style={css('font-family:var(--invoicecloud-font-family-mono);font-size:20px')}>${total.toFixed(2)}</span></div>
                      {(s.autopayOptIn || s.paperlessOptIn) && (
                        <div style={css('background:var(--invoicecloud-primary-tint);border-radius:10px;padding:var(--invoicecloud-spacing-s);font-size:13px')}>
                          <div style={css('font-weight:500;margin-bottom:4px')}>You're enrolling in</div>
                          {s.autopayOptIn && <div>AutoPay</div>}
                          {s.paperlessOptIn && <div>Paperless Billing</div>}
                        </div>
                      )}
                    </div>
                  </div>
                </>
              )}

              {s.payerStep === 2 && (
                <div style={css('text-align:center;padding:var(--invoicecloud-spacing-l) 0')}>
                  <img src={asset('assets/icons/Success.svg')} alt="" style={css('width:56px;height:56px;margin-bottom:var(--invoicecloud-spacing-s)')} />
                  <h2 style={css('margin-bottom:var(--invoicecloud-spacing-xxs)')}>Payment received</h2>
                  <p style={css('color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>A receipt was sent for ${total.toFixed(2)}.</p>
                  <div style={css('display:flex;justify-content:center;gap:var(--invoicecloud-spacing-xs);margin-bottom:var(--invoicecloud-spacing-l)')}>
                    {s.autopayOptIn && <span style={css('font-size:12px;font-weight:700;padding:6px 12px;border-radius:4px;background:var(--invoicecloud-intent-success-background);color:var(--invoicecloud-intent-success)')}>AutoPay enrolled</span>}
                    {s.paperlessOptIn && <span style={css('font-size:12px;font-weight:700;padding:6px 12px;border-radius:4px;background:var(--invoicecloud-intent-success-background);color:var(--invoicecloud-intent-success)')}>Paperless enabled</span>}
                  </div>
                  <button type="button" onClick={payerRestart} style={css('background:none;border:1px solid var(--invoicecloud-surface-default-border);border-radius:10px;padding:10px 20px;font-size:14px;cursor:pointer')}>View statements</button>
                </div>
              )}
            </div>
          </div>

          <div style={css(`width:100%;max-width:${previewMaxWidth};background:var(--invoicecloud-utility-neutral-90);color:#fff;border-radius:14px;padding:var(--invoicecloud-spacing-m) var(--invoicecloud-spacing-l);margin-top:var(--invoicecloud-spacing-m);display:flex;align-items:center;justify-content:space-between;gap:var(--invoicecloud-spacing-m);flex-wrap:wrap;animation:fadeUp .4s ease-out`)}>
            <div>
              <div style={css('font-weight:500;margin-bottom:2px')}>{billingInterviewPending(s.backendSession) ? 'Finish the billing interview before publishing.' : s.payerStep === 2 ? "That's what your payers will see." : 'Ready to publish this experience?'}</div>
              <div style={css('font-size:13px;opacity:.75')}>{billingInterviewPending(s.backendSession) ? billingInterviewPrompt(s.backendSession) : s.payerStep === 2 ? 'The payment journey is verified. Launch it now, or save it for later.' : 'You can publish at any point; completing the sample payment is optional.'}</div>
            </div>
            <div style={css('display:flex;gap:var(--invoicecloud-spacing-s);flex-wrap:wrap')}>
              <button type="button" onClick={openSignup} style={css('background:none;border:1px solid rgba(255,255,255,.3);color:#fff;border-radius:10px;padding:12px 20px;font-size:14px;cursor:pointer')}>Save without publishing</button>
              <button type="button" onClick={publishFromPreview} style={css('background:var(--invoicecloud-secondary);color:var(--invoicecloud-utility-neutral-100);border:none;border-radius:10px;padding:12px 24px;font-size:14px;font-weight:700;cursor:pointer')}>{billingInterviewPending(s.backendSession) ? 'Finish billing interview' : 'Publish →'}</button>
            </div>
          </div>
        </div>
      )}

      {/* ================= DASHBOARD ================= */}
      {s.screen === 'dashboard' && (
        <div style={css('min-height:100vh;display:flex')}>
          <nav style={css('width:240px;flex:none;background:var(--invoicecloud-slate-05);border-right:1px solid var(--invoicecloud-surface-default-border);padding:var(--invoicecloud-spacing-m);display:flex;flex-direction:column')}>
            <img src={asset('assets/pronto-logo.svg')} alt="Pronto" style={css('height:20px;margin-bottom:var(--invoicecloud-spacing-l)')} />
            {dashNav.map(renderNavItem)}
            <div style={css('flex:1')}></div>
            {renderNavItem({ id: 'help', label: 'Help', icon: 'Question' })}
          </nav>
          <main style={css('flex:1;padding:var(--invoicecloud-spacing-xl);max-width:1250px')}>
            <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-m)')}>
              <div><h2 style={css('margin-bottom:2px')}>Welcome back</h2><div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-60)')}>{accountEmail}</div></div>
              <span style={css('width:36px;height:36px;border-radius:50%;background:var(--invoicecloud-primary);color:#fff;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:13px')}>{initialsFromEmail(accountEmail)}</span>
            </div>

            {s.dashboardSection === 'home' && (
              <>
                {!s.purchased && (
                  <div style={css('display:flex;align-items:center;justify-content:space-between;background:var(--invoicecloud-intent-info-background);border:1px solid var(--invoicecloud-intent-info-border);border-radius:10px;padding:var(--invoicecloud-spacing-s) var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-l)')}>
                    <span style={css('font-size:14px;color:var(--invoicecloud-intent-info)')}>You're in draft mode - publish to make this live for payers.</span>
                    <button type="button" onClick={openCheckout} style={css('background:var(--invoicecloud-intent-info);color:#fff;border:none;border-radius:8px;padding:8px 16px;font-size:13px;font-weight:700;cursor:pointer')}>Publish</button>
                  </div>
                )}
                {s.purchased && s.deployment?.published_url && (
                  <div style={css('display:flex;align-items:center;justify-content:space-between;gap:16px;background:var(--invoicecloud-intent-success-background);border:1px solid var(--invoicecloud-intent-success-border);border-radius:10px;padding:var(--invoicecloud-spacing-s) var(--invoicecloud-spacing-m);margin-bottom:var(--invoicecloud-spacing-l)')}>
                    <span style={css('font-size:14px;color:var(--invoicecloud-intent-success)')}>Your payer experience is live.</span>
                    <a href={s.deployment.published_url} target="_blank" rel="noreferrer" style={css('font-size:13px;font-weight:700;color:var(--invoicecloud-intent-success)')}>Open payment site &rarr;</a>
                  </div>
                )}
                {linesOfBusinessGrid}
              </>
            )}

            {s.dashboardSection === 'lob' && (
              <>
                <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>Each line of business has its own branded payer experience, states, and payment settings.</p>
                {linesOfBusinessGrid}
              </>
            )}

            {s.dashboardSection === 'billing' && (
              <>
                <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Billing &amp; subscription</h3>
                <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-l);margin-bottom:var(--invoicecloud-spacing-m)')}>
                  <div style={css('display:flex;justify-content:space-between;align-items:center;gap:16px')}>
                    <div>
                      <div style={css('font-weight:700;font-size:15px')}>{s.purchased ? 'Pronto Publish' : 'Draft (no active plan)'}</div>
                      <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-top:2px')}>{s.purchased ? 'Your payer experiences can be published live.' : 'Publish a line of business to start your subscription.'}</div>
                    </div>
                    <div style={css('text-align:right')}>
                      <span style={css(`font-size:11px;font-weight:700;padding:4px 10px;border-radius:4px;background:${s.purchased ? 'var(--invoicecloud-intent-success-background)' : 'var(--invoicecloud-intent-neutral-background)'};color:${s.purchased ? 'var(--invoicecloud-intent-success)' : 'var(--invoicecloud-intent-neutral)'}`)}>{s.purchased ? 'Active' : 'Inactive'}</span>
                      <div style={css('font-weight:700;font-size:18px;margin-top:6px')}>$199<span style={css('font-size:13px;font-weight:500;color:var(--invoicecloud-utility-neutral-70)')}>/mo</span></div>
                    </div>
                  </div>
                  {!s.purchased && (
                    <button type="button" onClick={openCheckout} style={css('margin-top:var(--invoicecloud-spacing-m);background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:8px;padding:10px 18px;font-size:13px;font-weight:700;cursor:pointer')}>Publish &amp; subscribe</button>
                  )}
                </div>
                <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m) var(--invoicecloud-spacing-l)')}>
                  {settingsRow('Lines of business', String(s.lobs.length))}
                  {settingsRow('Live lines of business', String(liveLobCount))}
                  {settingsRow('Billing contact', accountEmail)}
                  {settingsRow('Payment method', s.purchased ? 'Card ending 4242 (demo)' : 'None on file')}
                </div>
                <p style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-top:var(--invoicecloud-spacing-s)')}>Demo placeholder - billing reflects your current session state; no real charges are made.</p>
              </>
            )}

            {s.dashboardSection === 'settings' && (
              <>
                <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Settings</h3>
                <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m) var(--invoicecloud-spacing-l);margin-bottom:var(--invoicecloud-spacing-m)')}>
                  <div style={css('font-weight:700;font-size:14px;margin-bottom:4px')}>Business profile</div>
                  {settingsRow('Business name', s.bizName || 'Not set')}
                  {settingsRow('Account email', accountEmail)}
                  {settingsRow('Primary vertical', verticalSummaryLabel)}
                  {settingsRow('Operating states', s.selectedStates.length ? s.selectedStates.join(', ') : 'None selected')}
                </div>
                <div style={css('background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m) var(--invoicecloud-spacing-l)')}>
                  <div style={css('font-weight:700;font-size:14px;margin-bottom:4px')}>Payment preferences</div>
                  {settingsRow('Guest checkout', guestCheckoutAllowedLabel)}
                  {settingsRow('Offer AutoPay', offerAutopayLabel)}
                  {settingsRow('Offer Paperless', offerPaperlessLabel)}
                  {settingsRow('Accepted methods', s.acceptedMethods.map((m) => methodLabels[m] ?? m).join(', ') || 'None')}
                </div>
                <p style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-top:var(--invoicecloud-spacing-s)')}>Demo placeholder - edit these values through the onboarding wizard for each line of business.</p>
              </>
            )}

            {s.dashboardSection === 'help' && (
              <>
                <h3 style={css('font-size:16px;margin-bottom:var(--invoicecloud-spacing-s)')}>Help &amp; support</h3>
                <div style={css('display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:var(--invoicecloud-spacing-m)')}>
                  {helpLinks.map((link) => (
                    <a key={link.title} href={link.href} target={link.href.startsWith('http') ? '_blank' : undefined} rel="noreferrer" style={css('display:block;background:var(--invoicecloud-surface-default-background);border:1px solid var(--invoicecloud-surface-default-border);border-radius:14px;padding:var(--invoicecloud-spacing-m);text-decoration:none;color:inherit')}>
                      <div style={css('font-weight:700;font-size:14px;margin-bottom:4px')}>{link.title}</div>
                      <div style={css('font-size:13px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-s)')}>{link.desc}</div>
                      <div style={css('font-size:13px;font-weight:700;color:var(--invoicecloud-primary)')}>{link.cta}</div>
                    </a>
                  ))}
                </div>
                <p style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);margin-top:var(--invoicecloud-spacing-s)')}>Demo placeholder - support links point at demo destinations.</p>
              </>
            )}
          </main>
        </div>
      )}
      </div>

      {/* ================= MODALS ================= */}
      {s.modal === 'signup' && (
        <div style={css('position:fixed;inset:0;background:rgba(28,28,28,.5);display:flex;align-items:center;justify-content:center;z-index:60;padding:var(--invoicecloud-spacing-l)')}>
          <form ref={signupDialogRef} role="dialog" aria-modal="true" aria-labelledby="signup-dialog-title" onSubmit={submitSignup} style={css('background:#fff;border-radius:14px;width:100%;max-width:420px;padding:var(--invoicecloud-spacing-l);box-shadow:var(--invoicecloud-elevation-3)')}>
            <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-m)')}>
              <h3 id="signup-dialog-title" style={css('font-size:18px')}>Create your account</h3>
              <button type="button" onClick={closeModal} aria-label="Close" style={css('background:none;border:none;cursor:pointer;padding:4px')}><img src={asset('assets/icons/MenuClose.svg')} alt="" style={css('width:14px;height:14px')} /></button>
            </div>
            <label htmlFor="signup-email" style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Work email</label>
            <input id="signup-email" data-autofocus type="email" required autoComplete="email" value={s.signupEmail} onChange={setSignupEmail} placeholder={`you@${siteSlug}.com`} style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-size:15px;margin-bottom:var(--invoicecloud-spacing-s)')} />
            <label htmlFor="signup-password" style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Password</label>
            <input id="signup-password" type="password" autoComplete="new-password" value={s.signupPassword} onChange={setSignupPassword} aria-invalid={s.signupError ? true : undefined} placeholder={`At least ${MIN_PASSWORD_LENGTH} characters`} style={css(`width:100%;padding:12px 14px;border-radius:4px;border:1px solid ${s.signupError ? 'var(--invoicecloud-intent-error-border)' : 'var(--invoicecloud-surface-default-border)'};font-size:15px;margin-bottom:${s.signupError ? 'var(--invoicecloud-spacing-s)' : 'var(--invoicecloud-spacing-l)'}`)} />
            {s.signupError && (
              <div role="alert" style={css('background:var(--invoicecloud-intent-error-background);border:1px solid var(--invoicecloud-intent-error-border);color:var(--invoicecloud-intent-error);border-radius:8px;padding:10px 12px;font-size:13px;margin-bottom:var(--invoicecloud-spacing-l)')}>{s.signupError}</div>
            )}
            <button type="submit" style={css('width:100%;background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:10px;padding:14px;font-size:15px;font-weight:700;cursor:pointer')}>Create Account &amp; Save Draft</button>
          </form>
        </div>
      )}

      {s.modal === 'checkout' && (
        <div style={css('position:fixed;inset:0;background:rgba(28,28,28,.5);display:flex;align-items:center;justify-content:center;z-index:60;padding:var(--invoicecloud-spacing-l)')}>
          <form ref={checkoutDialogRef} role="dialog" aria-modal="true" aria-labelledby="checkout-dialog-title" onSubmit={submitCheckout} style={css('background:#fff;border-radius:14px;width:100%;max-width:420px;padding:var(--invoicecloud-spacing-l);box-shadow:var(--invoicecloud-elevation-3)')}>
            <div style={css('display:flex;justify-content:space-between;align-items:center;margin-bottom:var(--invoicecloud-spacing-m)')}>
              <h3 id="checkout-dialog-title" style={css('font-size:18px')}>Publish {s.bizName}</h3>
              <button type="button" onClick={closeModal} aria-label="Close" style={css('background:none;border:none;cursor:pointer;padding:4px')}><img src={asset('assets/icons/MenuClose.svg')} alt="" style={css('width:14px;height:14px')} /></button>
            </div>
            <div style={css('background:var(--invoicecloud-primary-tint);border-radius:10px;padding:var(--invoicecloud-spacing-s);font-size:13px;margin-bottom:var(--invoicecloud-spacing-m);display:flex;justify-content:space-between')}><span>Pronto Publish</span><span style={css('font-weight:700')}>$199/mo</span></div>
            <label htmlFor="checkout-card-number" style={css('display:block;font-size:13px;font-weight:500;margin-bottom:4px')}>Card number</label>
            <input id="checkout-card-number" data-autofocus type="text" required inputMode="numeric" autoComplete="cc-number" placeholder="4242 4242 4242 4242" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono);margin-bottom:var(--invoicecloud-spacing-s)')} />
            <div style={css('display:flex;gap:var(--invoicecloud-spacing-s);margin-bottom:var(--invoicecloud-spacing-l)')}>
              <label htmlFor="checkout-expiry" style={css('display:flex;flex:1;flex-direction:column;gap:4px;font-size:13px;font-weight:500')}>Expiration
                <input id="checkout-expiry" type="text" required inputMode="numeric" autoComplete="cc-exp" placeholder="MM/YY" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono)')} />
              </label>
              <label htmlFor="checkout-cvc" style={css('display:flex;flex:1;flex-direction:column;gap:4px;font-size:13px;font-weight:500')}>Security code
                <input id="checkout-cvc" type="text" required inputMode="numeric" autoComplete="cc-csc" placeholder="CVC" style={css('width:100%;padding:12px 14px;border-radius:4px;border:1px solid var(--invoicecloud-surface-default-border);font-family:var(--invoicecloud-font-family-mono)')} />
              </label>
            </div>
            {(s.publishing || s.deployment) && (
              <div role="status" aria-live="polite" style={css('background:var(--invoicecloud-primary-tint);border-radius:8px;padding:10px 12px;font-size:13px;margin-bottom:var(--invoicecloud-spacing-s)')}>
                {s.publishing ? `Publishing: ${(s.deployment?.state || 'preparing').replaceAll('_', ' ')}...` : `Publication status: ${s.deployment?.state.replaceAll('_', ' ')}`}
              </div>
            )}
            {s.publishError && (
              <div role="alert" style={css('background:var(--invoicecloud-intent-error-background);border:1px solid var(--invoicecloud-intent-error-border);color:var(--invoicecloud-intent-error);border-radius:8px;padding:10px 12px;font-size:13px;margin-bottom:var(--invoicecloud-spacing-s)')}>
                <strong>Could not publish.</strong> {s.publishError.message}
                {s.publishError.findings.length > 0 && (
                  <ul style={css('margin:8px 0 0;padding-left:20px')}>
                    {s.publishError.findings.map(finding => <li key={finding.code}>{finding.message}</li>)}
                  </ul>
                )}
                {s.publishError.reference && <small style={css('display:block;margin-top:8px')}>Technical reference: {s.publishError.reference}</small>}
              </div>
            )}
            <button type="submit" disabled={s.publishing} style={css(`width:100%;background:var(--invoicecloud-intent-success-hover);color:#fff;border:none;border-radius:10px;padding:14px;font-size:15px;font-weight:700;cursor:${s.publishing ? 'wait' : 'pointer'};opacity:${s.publishing ? '.65' : '1'}`)}>{s.publishing ? 'Publishing...' : s.deployment && !['failed', 'rolled_back', 'ready'].includes(s.deployment.state.toLowerCase()) ? 'Check Publish Status' : 'Pay & Publish'}</button>
            <div style={css('font-size:12px;color:var(--invoicecloud-utility-neutral-60);text-align:center;margin-top:var(--invoicecloud-spacing-s)')}>Fake checkout - no real card is charged in this demo.</div>
          </form>
        </div>
      )}

      {s.reviewSaveError && (
        <div style={css('position:fixed;inset:0;background:rgba(28,28,28,.5);display:flex;align-items:center;justify-content:center;z-index:70;padding:var(--invoicecloud-spacing-l)')}>
          <div role="alertdialog" aria-modal="true" style={css('background:#fff;border-radius:14px;width:100%;max-width:400px;padding:var(--invoicecloud-spacing-l);box-shadow:var(--invoicecloud-elevation-3);text-align:center')}>
            <img src={asset('assets/icons/Warning.svg')} alt="" style={css('width:40px;height:40px;margin-bottom:var(--invoicecloud-spacing-s)')} />
            <h3 style={css('font-size:18px;margin-bottom:var(--invoicecloud-spacing-xxs)')}>Save your changes first</h3>
            <p style={css('font-size:14px;color:var(--invoicecloud-utility-neutral-70);margin-bottom:var(--invoicecloud-spacing-m)')}>You have an open edit section. Save it before previewing your payment site.</p>
            <button type="button" onClick={closeSaveErrorModal} style={css('background:var(--invoicecloud-primary);color:#fff;border:none;border-radius:10px;padding:12px 28px;font-size:15px;font-weight:700;cursor:pointer')}>Got it</button>
          </div>
        </div>
      )}
    </div>
  );
}
