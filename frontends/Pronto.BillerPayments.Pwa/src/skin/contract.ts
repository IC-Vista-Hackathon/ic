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
