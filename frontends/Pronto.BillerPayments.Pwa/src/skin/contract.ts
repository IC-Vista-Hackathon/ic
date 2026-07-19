import type { ExperienceDefinition } from '../types';

// The skin is the ONLY surface an AI generator is allowed to rewrite per biller.
// It controls look and feel (theme.css) and the branded chrome (header, intro, footer)
// through this typed contract. It must never contain payment flow, service calls, or
// business logic — those live in the stable core (App.tsx) and are validated by the
// Playwright gate against fixed data-testids the skin does not own.

export type SkinBrand = ExperienceDefinition['brand'];
export type SkinContent = ExperienceDefinition['content'];

export type SkinPage = 'payment' | 'history' | 'preferences';

export interface SkinNavItem {
  page: SkinPage;
  label: string;
  active: boolean;
  onSelect: () => void;
}

export interface HeaderProps {
  brand: SkinBrand;
}

export interface IntroProps {
  eyebrow: string;
  heading: string;
  subheading: string;
}

export interface FooterProps {
  brand: SkinBrand;
  content: SkinContent;
}

// ---------------------------------------------------------------------------
// Multi-invoice selection + cart + batch checkout — authorable STRUCTURE (F3).
//
// These components let a biller with multiple bill types get a structurally
// different payer experience: a selectable invoice list, a cart, and a batch
// review of everything being paid. They are PRESENTATIONAL ONLY — the stable
// core (App.tsx / provider.ts) does all money math, quoting, and settlement and
// hands these components fully-computed, preformatted view models plus callbacks.
// A generated flow must never fetch, compute a fee/total, or invoke a payment;
// it only renders the view models and calls the provided callbacks. The core
// keeps the fixed data-testids the gate drives; a skin may add its own.
// ---------------------------------------------------------------------------

// One open invoice the payer can add to / remove from the batch. Every money and
// date value is preformatted by the core; the skin renders strings, never numbers.
export interface SelectableInvoice {
  id: string;
  // Bill-type label (e.g. "Water", "Sewer") when the biller has multiple types.
  typeLabel?: string;
  description: string;
  dueDateLabel: string;
  amountLabel: string;
  // Optional status affordance the core computes (color + human label).
  statusColor?: string;
  statusLabel?: string;
  note?: string;
  noteEmphasis?: boolean;
  // Whether this invoice is currently in the batch.
  selected: boolean;
}

export interface InvoiceSelectListProps {
  heading: string;
  invoices: SelectableInvoice[];
  // Toggle a single invoice in/out of the batch.
  onToggle: (invoiceId: string) => void;
  // Bulk helpers; the core owns the actual selection set.
  onSelectAll: () => void;
  onClearAll: () => void;
  allSelected: boolean;
}

// A single line in the cart / batch, with preformatted money labels.
export interface CartLine {
  id: string;
  label: string;
  typeLabel?: string;
  amountLabel: string;
}

// Cart totals, all preformatted by the core. `feeLabel` is undefined until a
// payment method is chosen (fees are quoted server-side per method).
export interface CartSummary {
  lines: CartLine[];
  count: number;
  subtotalLabel: string;
  feeLabel?: string;
  totalLabel: string;
}

export interface CartProps {
  summary: CartSummary;
  // Present when the payer may still drop a line before checkout. Removing a line
  // is a selection change; the core owns the selection set and does the math.
  onRemove?: (invoiceId: string) => void;
  // Copy explaining the empty state (no invoices selected).
  emptyText: string;
}

export type BatchLineStatus = 'pending' | 'paid' | 'failed';

// A per-invoice line in the batch review / result. Money labels are preformatted;
// `status`/`statusMessage` are set by the core as settlement progresses.
export interface BatchReviewLine {
  id: string;
  label: string;
  typeLabel?: string;
  amountLabel: string;
  // Self-contained payer-facing fee label (e.g. "$2.50 fee", "No payer fee", or
  // "…" while quoting). Render it as-is — never append the word "fee" yourself.
  feeLabel: string;
  totalLabel: string;
  status: BatchLineStatus;
  statusMessage?: string;
}

export interface BatchReviewProps {
  heading: string;
  lines: BatchReviewLine[];
  totalLabel: string;
  consentText: string;
}

// ---------------------------------------------------------------------------
// Amount-entry + installment-plan chooser — authorable STRUCTURE (F4).
//
// A biller whose policy allows it gets a structurally different journey: pay a
// partial amount or enroll in an installment plan instead of only the full
// balance. PRESENTATIONAL ONLY. The stable core derives eligibility from the
// biller configuration, parses/validates the typed amount, computes every money
// label, and drives the request; this component only renders controls and calls
// back. The Payment Service remains the sole authority on amounts — it
// re-validates the requested amount and plan against the invoice balance and the
// biller's policy, so nothing here can move money or invent a total.
// ---------------------------------------------------------------------------

export type PaymentPlanMode = 'full' | 'partial' | 'installment';

// One installment-count choice with a core-preformatted estimate label.
export interface InstallmentOption {
  count: number;
  // e.g. "3 monthly payments of about $34.67". Render as-is; never recompute.
  label: string;
}

export interface PaymentPlanChooserProps {
  // Policy-gated visibility; when both are false the core renders nothing and the
  // default full-payment journey stands.
  allowPartial: boolean;
  allowInstallments: boolean;
  // Currently-selected journey (controlled by the core).
  mode: PaymentPlanMode;
  onModeChange: (mode: PaymentPlanMode) => void;
  // Full-balance option label, e.g. "Pay the full balance — $104.00".
  fullLabel: string;
  // Partial-amount entry. `amountValue` is the raw controlled string; the core
  // parses and validates it — the skin never converts it to a number.
  amountValue: string;
  onAmountChange: (value: string) => void;
  // e.g. "Enter an amount up to $104.00". `amountError` is a core-produced message.
  amountHint: string;
  amountError?: string;
  // Installment choices the core derived from the biller's max-installments policy.
  installmentOptions: InstallmentOption[];
  selectedInstallmentCount?: number;
  onInstallmentCountChange: (count: number) => void;
}
