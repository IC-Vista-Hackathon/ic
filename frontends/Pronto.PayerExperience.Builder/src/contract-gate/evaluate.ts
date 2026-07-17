import {
  CLIENT_AMOUNT_KEYS,
  CLIENT_FEE_KEYS,
  IDEMPOTENCY_HEADER,
  IDEMPOTENCY_KEY_PATTERN,
  type ContractObservations,
  type ContractViolation,
  type PaymentRequestObservation,
} from './contract';

// Pure conformance check: given what the driven flow emitted, return the set of contract
// violations. No browser, no I/O — this is the single place the F6 invariants are decided, so
// it can be unit-tested exhaustively and reused by the runtime gate.
export function evaluateContract(observations: ContractObservations): ContractViolation[] {
  const violations: ContractViolation[] = [];

  if (observations.flowError) {
    violations.push({
      code: 'flow-error',
      message: `The generated flow could not be driven to a payment via accessibility roles: ${observations.flowError}`,
      detail: observations.flowError,
    });
  }

  const posts = observations.paymentPosts;

  if (posts.length === 0) {
    // Without a payment request there is nothing to conform to — but a rendered confirmation
    // with no charge is itself a serious violation (a fabricated confirmation).
    violations.push({
      code: 'no-payment-request',
      message:
        'Confirming the payment emitted no POST /payments request; the generated flow never invoked the sanctioned payment contract.',
    });
    checkConfirmation(observations, violations);
    return violations;
  }

  if (posts.length > 1) {
    violations.push({
      code: 'multiple-payment-requests',
      message: `A single user confirmation emitted ${posts.length} POST /payments requests; the flow must be double-submit safe (exactly one charge per confirmation).`,
      detail: `paymentRequestCount=${posts.length}`,
    });
  }

  // Field-level invariants: check every emitted request, but report each violation code once.
  const seen = new Set<string>();
  const addOnce = (violation: ContractViolation) => {
    if (seen.has(violation.code)) return;
    seen.add(violation.code);
    violations.push(violation);
  };

  for (const post of posts) {
    checkIdempotency(post, addOnce);
    checkNoClientMoney(post, addOnce);
    checkInvoiceReference(post, observations.knownInvoiceIds, addOnce);
  }

  checkConfirmation(observations, violations);

  return violations;
}

function checkIdempotency(post: PaymentRequestObservation, add: (v: ContractViolation) => void): void {
  const key = post.headers[IDEMPOTENCY_HEADER];
  if (!key) {
    add({
      code: 'missing-idempotency-key',
      message: `The payment POST is missing the required "${IDEMPOTENCY_HEADER}" header; the charge would not be exactly-once.`,
    });
    return;
  }
  if (!IDEMPOTENCY_KEY_PATTERN.test(key)) {
    add({
      code: 'malformed-idempotency-key',
      message: `The payment POST "${IDEMPOTENCY_HEADER}" header "${key}" is not a well-formed idempotency key (expected a UUID).`,
      detail: key,
    });
  }
}

function checkNoClientMoney(post: PaymentRequestObservation, add: (v: ContractViolation) => void): void {
  const body = post.body ?? {};
  const amountKey = CLIENT_AMOUNT_KEYS.find(key => key in body);
  if (amountKey) {
    add({
      code: 'client-controlled-amount',
      message: `The payment POST body sets a client-controlled amount ("${amountKey}"=${JSON.stringify(body[amountKey])}); the amount must come from the invoice server-side, never the client.`,
      detail: amountKey,
    });
  }
  const feeKey = CLIENT_FEE_KEYS.find(key => key in body);
  if (feeKey) {
    add({
      code: 'client-controlled-fee',
      message: `The payment POST body sets a client-controlled fee/total ("${feeKey}"=${JSON.stringify(body[feeKey])}); fees and totals are computed server-side from the quote, never sent by the client.`,
      detail: feeKey,
    });
  }
}

function checkInvoiceReference(
  post: PaymentRequestObservation,
  knownInvoiceIds: string[],
  add: (v: ContractViolation) => void,
): void {
  const invoiceId = post.body && typeof post.body.invoice_id === 'string' ? post.body.invoice_id : undefined;
  if (!invoiceId) {
    add({
      code: 'missing-invoice-reference',
      message: 'The payment POST body does not reference an invoice ("invoice_id"); the charge is not bound to a real bill.',
    });
    return;
  }
  if (!knownInvoiceIds.includes(invoiceId)) {
    add({
      code: 'fabricated-invoice-reference',
      message: `The payment POST references invoice "${invoiceId}", which was never returned by the invoice lookup; the flow fabricated an invoice id.`,
      detail: invoiceId,
    });
  }
}

function checkConfirmation(observations: ContractObservations, violations: ContractViolation[]): void {
  const rendered = observations.renderedConfirmation;
  const server = observations.serverConfirmation;
  if (rendered !== server) {
    violations.push({
      code: 'confirmation-mismatch',
      message: `The confirmation the UI rendered (${rendered === null ? 'none' : `"${rendered}"`}) does not equal the confirmation "${server}" the payment service returned; a fabricated/hallucinated confirmation is not permitted.`,
      detail: rendered ?? '',
    });
  }
}

// One-line human summary for pipeline logs, matching the F5 gate's summarizeReport style.
export function summarizeContractReport(report: { passed: boolean; paymentRequestCount: number; violations: ContractViolation[] }): string {
  if (report.passed) {
    return `contract gate PASSED (1 confirmation → ${report.paymentRequestCount} payment POST, all payment-contract invariants satisfied)`;
  }
  const lines = report.violations.map(violation => `  - [${violation.code}] ${violation.message}`);
  return `contract gate FAILED with ${report.violations.length} violation(s):\n${lines.join('\n')}`;
}
