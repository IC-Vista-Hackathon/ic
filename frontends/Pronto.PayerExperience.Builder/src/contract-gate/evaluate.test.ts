import assert from 'node:assert/strict';
import { test } from 'node:test';
import { evaluateContract, summarizeContractReport } from './evaluate';
import type { ContractObservations, PaymentRequestObservation } from './contract';

const SERVER_CONFIRMATION = 'PRONTO-ABC123';

function compliantPost(overrides: Partial<PaymentRequestObservation> = {}): PaymentRequestObservation {
  return {
    url: 'http://localhost/payments',
    method: 'POST',
    headers: { 'idempotency-key': '11111111-2222-4333-8444-555555555555' },
    body: { biller_id: 'biller-1', invoice_id: 'inv-1', method: 'card', payer_account_id: null, scheduled_for: null },
    rawBody: '{}',
    ...overrides,
  };
}

function observations(overrides: Partial<ContractObservations> = {}): ContractObservations {
  return {
    knownInvoiceIds: ['inv-1'],
    serverConfirmation: SERVER_CONFIRMATION,
    renderedConfirmation: SERVER_CONFIRMATION,
    paymentPosts: [compliantPost()],
    ...overrides,
  };
}

test('a compliant flow passes with no violations', () => {
  const violations = evaluateContract(observations());
  assert.deepEqual(violations, []);
});

test('a client-controlled amount fails', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost({ body: { invoice_id: 'inv-1', amount_cents: 999 } })] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'client-controlled-amount');
  assert.match(violations[0].message, /amount must come from the invoice/i);
});

test('a client-controlled fee/total fails', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost({ body: { invoice_id: 'inv-1', total_cents: 12750 } })] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'client-controlled-fee');
  assert.match(violations[0].message, /computed server-side/i);
});

test('a missing idempotency key fails', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost({ headers: {} })] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'missing-idempotency-key');
});

test('a malformed idempotency key fails', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost({ headers: { 'idempotency-key': 'not-a-uuid' } })] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'malformed-idempotency-key');
});

test('two payment POSTs for one confirmation fails (double-submit)', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost(), compliantPost()] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'multiple-payment-requests');
  assert.match(violations[0].message, /double-submit safe/i);
});

test('no payment POST fails', () => {
  const violations = evaluateContract(observations({ paymentPosts: [], renderedConfirmation: null }));
  assert.ok(violations.some(violation => violation.code === 'no-payment-request'));
});

test('a fabricated confirmation fails', () => {
  const violations = evaluateContract(observations({ renderedConfirmation: 'PRONTO-HALLUCINATED' }));
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'confirmation-mismatch');
  assert.match(violations[0].message, /fabricated\/hallucinated confirmation/i);
});

test('a fabricated invoice id fails', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost({ body: { invoice_id: 'inv-FAKE' } })] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'fabricated-invoice-reference');
});

test('a missing invoice reference fails', () => {
  const violations = evaluateContract(
    observations({ paymentPosts: [compliantPost({ body: { method: 'card' } })] }),
  );
  assert.equal(violations.length, 1);
  assert.equal(violations[0].code, 'missing-invoice-reference');
});

test('a flow that cannot be driven fails', () => {
  const violations = evaluateContract(observations({ paymentPosts: [], renderedConfirmation: null, flowError: 'timeout' }));
  assert.ok(violations.some(violation => violation.code === 'flow-error'));
});

test('summarizeContractReport reports pass and fail clearly', () => {
  const pass = summarizeContractReport({ passed: true, paymentRequestCount: 1, violations: [] });
  assert.match(pass, /contract gate PASSED/);
  const fail = summarizeContractReport({
    passed: false,
    paymentRequestCount: 1,
    violations: evaluateContract(observations({ renderedConfirmation: 'x' })),
  });
  assert.match(fail, /contract gate FAILED/);
  assert.match(fail, /confirmation-mismatch/);
});
