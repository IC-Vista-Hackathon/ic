// Runtime boundary / payment-contract conformance gate contract (feature F6).
//
// This module is the SINGLE SOURCE OF TRUTH for what the REQUESTS a generated payer flow
// emits must look like. Where the F5 static gate (src/gate/) proves the generated *source*
// stays inside the authorable allowlist, this gate proves the built *bundle* correctly
// INVOKES the sanctioned payment contract at runtime — it mounts the bundle, drives the flow
// by accessibility roles (so it survives a widened, generated structure with no fixed
// data-testids), intercepts the network calls, and asserts the emitted requests conform.
//
// The invariants below mirror the server-authoritative money rules enforced by
// services/Pronto.Payment.Api/Controllers/PaymentsController.cs: the client never chooses the
// amount or fee, every charge is idempotent (exactly-once), and the confirmation the payer
// sees is the one the service issued — never a fabricated/hallucinated value.

// Request-body keys that would mean the client is trying to control the money amount. The
// payment amount must always come from the invoice server-side, never the request body.
export const CLIENT_AMOUNT_KEYS = ['amount', 'amount_cents', 'amount_due', 'amount_due_cents'] as const;

// Request-body keys that would mean the client is trying to control the fee / total. Fees and
// totals are computed server-side (FeeCalculator) from the quote, never sent by the client.
export const CLIENT_FEE_KEYS = ['fee', 'fee_cents', 'total', 'total_cents', 'service_fee', 'service_fee_cents'] as const;

// The header the payment POST must carry so the charge is exactly-once.
export const IDEMPOTENCY_HEADER = 'idempotency-key';

// A well-formed idempotency key is a v4-style UUID (36 chars, hex + dashes) — the shape the
// PWA's randomId() produces and the shape the service expects.
export const IDEMPOTENCY_KEY_PATTERN = /^[0-9a-f-]{36}$/i;

export type ContractViolationCode =
  | 'no-payment-request'
  | 'multiple-payment-requests'
  | 'missing-idempotency-key'
  | 'malformed-idempotency-key'
  | 'client-controlled-amount'
  | 'client-controlled-fee'
  | 'missing-invoice-reference'
  | 'fabricated-invoice-reference'
  | 'confirmation-mismatch'
  | 'flow-error';

export interface ContractViolation {
  code: ContractViolationCode;
  message: string;
  // Optional machine-readable detail (offending key/value, counts, ...).
  detail?: string;
}

// A single payment POST the driven flow emitted, as captured at the network boundary.
export interface PaymentRequestObservation {
  url: string;
  method: string;
  headers: Record<string, string>;
  // Parsed JSON body when the body was JSON, otherwise null.
  body: Record<string, unknown> | null;
  rawBody: string | null;
}

// Everything the gate observed while driving one user-confirmation through the flow. This is
// the pure input to evaluateContract — decoupled from Playwright so the rules are unit-testable
// without a browser.
export interface ContractObservations {
  // Invoice ids the mocked lookup actually served — the only "real" ids a compliant flow may
  // reference in its payment request.
  knownInvoiceIds: string[];
  // The confirmation the mocked payment service returned in its response.
  serverConfirmation: string;
  // The confirmation the UI actually rendered to the payer (best-effort extracted), or null.
  renderedConfirmation: string | null;
  // Every POST /payments the flow emitted for the single confirmation.
  paymentPosts: PaymentRequestObservation[];
  // Set when the flow could not be driven to a payment (navigation/timeout/structure error).
  flowError?: string;
}

export interface ContractGateReport {
  passed: boolean;
  generatedAt: string;
  // How the flow was exercised — always accessibility roles/labels for this gate, never
  // fixed data-testids, so it survives a generated authorable structure.
  drivenBy: 'accessibility-roles';
  // Number of POST /payments requests observed for the single user confirmation.
  paymentRequestCount: number;
  // The server confirmation the gate matched the rendered UI against.
  serverConfirmation: string;
  violations: ContractViolation[];
}
