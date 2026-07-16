import { createServer, type Server } from 'node:http';
import { expect, test } from '@playwright/test';
import { runContractGateOnPage } from '../src/contract-gate';

// Fixture-driven proof of the F6 runtime contract gate. Each fixture is a self-contained
// "generated" payer flow that exposes the SAME accessibility roles the real bundle does
// (an "Account number" textbox, "Continue"/"Card"/"Review Payment"/"Pay …" buttons, and a
// rendered confirmation) but emits DIFFERENT payment requests. The gate drives each purely by
// role and asserts the emitted requests conform to the payment contract — a compliant flow
// passes, and each misbehaving flow fails with a specific violation.

type Variant =
  | 'compliant'
  | 'client-amount'
  | 'client-fee'
  | 'no-idempotency'
  | 'double-post'
  | 'hallucinated-confirmation'
  | 'fabricated-invoice';

const definition = { biller_id: 'biller-1' };

function fixtureHtml(variant: Variant): string {
  return `<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>Generated payer flow (${variant})</title></head>
<body>
  <main>
    <section id="lookup">
      <label>Account number <input id="account" autocomplete="off" /></label>
      <button id="continue" type="button">Continue</button>
    </section>
    <section id="method" hidden>
      <button id="card" type="button">Card <small>No payer fee</small></button>
      <button id="to-review" type="button">Review Payment</button>
    </section>
    <section id="review" hidden>
      <button id="pay" type="button">Pay $127.50</button>
    </section>
    <section id="done" hidden aria-live="polite">
      <p>Confirmation <strong id="code"></strong></p>
    </section>
  </main>
  <script>
    const VARIANT = ${JSON.stringify(variant)};
    let invoiceId = null;
    const show = id => ['lookup', 'method', 'review', 'done'].forEach(s => {
      document.getElementById(s).hidden = s !== id;
    });
    document.getElementById('continue').addEventListener('click', async () => {
      const account = encodeURIComponent(document.getElementById('account').value);
      const res = await fetch('/invoices/billers/biller-1/invoices?account_number=' + account);
      const data = await res.json();
      invoiceId = data.invoices[0].id;
      show('method');
    });
    document.getElementById('to-review').addEventListener('click', () => show('review'));
    document.getElementById('pay').addEventListener('click', async () => {
      const body = {
        biller_id: 'biller-1',
        invoice_id: VARIANT === 'fabricated-invoice' ? 'inv-FAKE' : invoiceId,
        method: 'card',
        payer_account_id: null,
        scheduled_for: null,
      };
      if (VARIANT === 'client-amount') body.amount_cents = 12500;
      if (VARIANT === 'client-fee') body.total_cents = 12750;
      const headers = { 'content-type': 'application/json' };
      if (VARIANT !== 'no-idempotency') headers['idempotency-key'] = crypto.randomUUID();
      const post = () => fetch('/payments', { method: 'POST', headers, body: JSON.stringify(body) });
      const res = await post();
      if (VARIANT === 'double-post') await post();
      const payment = await res.json();
      document.getElementById('code').textContent =
        VARIANT === 'hallucinated-confirmation' ? 'PRONTO-HALLUCINATED' : payment.confirmation;
      show('done');
    });
  </script>
</body>
</html>`;
}

let server: Server;
let base = '';

test.beforeAll(async () => {
  server = createServer((request, response) => {
    const variant = (new URL(request.url ?? '/', 'http://fixture').pathname.slice(1) || 'compliant') as Variant;
    response.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    response.end(fixtureHtml(variant));
  });
  await new Promise<void>(resolve => server.listen(0, resolve));
  const address = server.address();
  base = `http://127.0.0.1:${typeof address === 'object' && address ? address.port : 0}`;
});

test.afterAll(async () => {
  await new Promise<void>(resolve => server.close(() => resolve()));
});

test('a compliant generated flow passes the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/compliant`, definition });
  expect(report.violations, JSON.stringify(report.violations)).toEqual([]);
  expect(report.passed).toBe(true);
  expect(report.paymentRequestCount).toBe(1);
});

test('a client-controlled amount fails the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/client-amount`, definition });
  expect(report.passed).toBe(false);
  expect(report.violations.map(v => v.code)).toContain('client-controlled-amount');
});

test('a client-controlled fee/total fails the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/client-fee`, definition });
  expect(report.passed).toBe(false);
  expect(report.violations.map(v => v.code)).toContain('client-controlled-fee');
});

test('a missing idempotency key fails the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/no-idempotency`, definition });
  expect(report.passed).toBe(false);
  expect(report.violations.map(v => v.code)).toContain('missing-idempotency-key');
});

test('a double-submit fails the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/double-post`, definition });
  expect(report.passed).toBe(false);
  expect(report.paymentRequestCount).toBeGreaterThan(1);
  expect(report.violations.map(v => v.code)).toContain('multiple-payment-requests');
});

test('a fabricated/hallucinated confirmation fails the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/hallucinated-confirmation`, definition });
  expect(report.passed).toBe(false);
  expect(report.violations.map(v => v.code)).toContain('confirmation-mismatch');
});

test('a fabricated invoice reference fails the contract gate', async ({ page }) => {
  const report = await runContractGateOnPage(page, { url: `${base}/fabricated-invoice`, definition });
  expect(report.passed).toBe(false);
  expect(report.violations.map(v => v.code)).toContain('fabricated-invoice-reference');
});
