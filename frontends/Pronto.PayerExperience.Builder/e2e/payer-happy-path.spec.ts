import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

const url = requiredEnvironment('PAYER_URL');
const configPath = requiredEnvironment('PAYER_CONFIG');

const definition = JSON.parse(readFileSync(configPath, 'utf8'));
const invoice = {
  id: 'inv-1',
  account_number: '4421',
  payer_name: 'Test Payer',
  amount_cents: 12500,
  due_date: '2026-08-01',
  description: 'Monthly bill',
  status: 'due',
};
const payer = {
  payer_id: 'payer-1',
  biller_id: definition.biller_id,
  name: 'Test Payer',
  email: 'payer@example.com',
  account_numbers: ['4421'],
  preferences: { autopay: false, paperless: false, channels: ['email'], payment_day: null },
};

async function mockConfig(page: Page, config = definition) {
  await page.route(/\/api\/public\/experiences\//, route =>
    route.request().url().includes('manifest')
      ? route.fulfill({ contentType: 'application/manifest+json', body: '{}' })
      : route.fulfill({ contentType: 'application/json', body: JSON.stringify(config) }),
  );
}

async function mockInvoice(page: Page) {
  await page.route(/\/invoices\//, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ invoices: [invoice] }) }),
  );
}

const multiInvoices = [
  { ...invoice, id: 'inv-1', description: 'Water service', amount_cents: 12500, type: 'Water' },
  { ...invoice, id: 'inv-2', description: 'Sewer service', amount_cents: 8000, type: 'Sewer' },
  { ...invoice, id: 'inv-3', description: 'Trash service', amount_cents: 4500, type: 'Trash' },
];

async function mockInvoices(page: Page, invoices = multiInvoices) {
  await page.route(/\/invoices\//, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ invoices }) }),
  );
}

async function mockQuotes(page: Page) {
  await page.route(/\/payments\/quote/, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ fee_cents: 250, total_cents: 12750 }) }),
  );
}

async function lookup(page: Page) {
  await page.goto(url);
  await page.getByTestId('account-input').fill('4421');
  await page.getByTestId('lookup-submit').click();
  await expect(page.getByTestId('method-card')).toBeVisible();
}

async function openCardReview(page: Page) {
  await page.getByTestId('method-card').click();
  await expect(page.getByTestId('review-submit')).toBeEnabled();
  await page.getByTestId('review-submit').click();
  await expect(page.getByTestId('pay-submit')).toBeEnabled();
}

test('payer completes lookup and one confirmation creates one payment', async ({ page }) => {
  let paymentPosts = 0;
  let idempotencyKey = '';
  await mockConfig(page);
  await mockInvoice(page);
  await page.route(/\/payers(\?|\/|$)/, route => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  await mockQuotes(page);
  await page.route(/\/payments(\?|$)/, async route => {
    paymentPosts += 1;
    idempotencyKey = route.request().headers()['idempotency-key'] ?? '';
    await new Promise(resolve => setTimeout(resolve, 100));
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ confirmation: 'PRONTO-ABC123', amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' }),
    });
  });

  await lookup(page);
  await openCardReview(page);
  await page.evaluate("const button = document.querySelector('[data-testid=\"pay-submit\"]'); button.click(); button.click();");

  await expect(page.getByTestId('payment-confirmation')).toBeVisible();
  await expect(page.getByTestId('confirmation-code')).toHaveText('PRONTO-ABC123');
  expect(paymentPosts).toBe(1);
  expect(idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
});

test('quote failures stay on method selection and can be retried', async ({ page }) => {
  let quoteRequests = 0;
  await mockConfig(page);
  await mockInvoice(page);
  await page.route(/\/payers(\?|\/|$)/, route => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  await page.route(/\/payments\/quote/, route => {
    quoteRequests += 1;
    if (quoteRequests <= 2) return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'quote unavailable' }) });
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ fee_cents: 250, total_cents: 12750 }) });
  });

  await lookup(page);
  await expect(page.getByTestId('quote-error')).toContainText('quote unavailable');
  await expect(page.getByTestId('review-submit')).toBeDisabled();
  await page.getByRole('button', { name: 'Retry quote' }).click();
  await expect(page.getByTestId('quote-error')).toHaveCount(0);
  await expect(page.getByTestId('review-submit')).toBeEnabled();
});

test('unsupported-only payment configuration fails recoverably', async ({ page }) => {
  await mockConfig(page, { ...definition, enabled_payment_capabilities: ['applepay'] });
  await page.goto(url);
  await expect(page.getByRole('heading', { name: 'Payment experience unavailable' })).toBeVisible();
  await expect(page.getByText(/does not include a supported payment method/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
});

test('malformed nested UI configuration fails recoverably', async ({ page }) => {
  await mockConfig(page, { ...definition, ui: { layout: 'broken' } });
  await page.goto(url);
  await expect(page.getByRole('heading', { name: 'Payment experience unavailable' })).toBeVisible();
  await expect(page.getByText(/interface configuration is invalid/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
});

test('failed payment does not mutate payer preferences', async ({ page }) => {
  let preferencePatches = 0;
  await mockConfig(page);
  await mockInvoice(page);
  await mockQuotes(page);
  await page.route(/\/payers(\?|\/|$)/, route => {
    if (route.request().method() === 'PATCH') {
      preferencePatches += 1;
      return route.fulfill({ contentType: 'application/json', body: JSON.stringify(payer.preferences) });
    }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(payer) });
  });
  await page.route(/\/payments(\?|$)/, route =>
    route.request().method() === 'GET'
      ? route.fulfill({ contentType: 'application/json', body: '[]' })
      : route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'processor unavailable' }) }),
  );

  await lookup(page);
  await page.getByRole('checkbox', { name: /Enroll in AutoPay/ }).check();
  await openCardReview(page);
  await page.getByTestId('pay-submit').click();
  await expect(page.getByTestId('error')).toContainText('processor unavailable');
  expect(preferencePatches).toBe(0);
});

test('post-payment preference failure preserves confirmation', async ({ page }) => {
  await mockConfig(page);
  await mockInvoice(page);
  await mockQuotes(page);
  await page.route(/\/payers(\?|\/|$)/, route =>
    route.request().method() === 'PATCH'
      ? route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'preferences unavailable' }) })
      : route.fulfill({ contentType: 'application/json', body: JSON.stringify(payer) }),
  );
  await page.route(/\/payments(\?|$)/, route =>
    route.request().method() === 'GET'
      ? route.fulfill({ contentType: 'application/json', body: '[]' })
      : route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ confirmation: 'PRONTO-PAID', amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' }),
      }),
  );

  await lookup(page);
  await page.getByRole('checkbox', { name: /Enroll in AutoPay/ }).check();
  await openCardReview(page);
  await page.getByTestId('pay-submit').click();
  await expect(page.getByTestId('payment-confirmation')).toBeVisible();
  await expect(page.getByTestId('confirmation-code')).toHaveText('PRONTO-PAID');
  await expect(page.getByTestId('error')).toContainText('Payment completed, but optional preferences could not be saved');
});

test('multi-invoice cart settles each selected invoice with its own idempotency key', async ({ page }) => {
  const posts: Array<{ invoiceId: string; key: string }> = [];
  await mockConfig(page);
  await mockInvoices(page);
  await page.route(/\/payers(\?|\/|$)/, route => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  await mockQuotes(page);
  await page.route(/\/payments(\?|$)/, route => {
    const body = route.request().postDataJSON() as { invoice_id: string };
    posts.push({ invoiceId: body.invoice_id, key: route.request().headers()['idempotency-key'] ?? '' });
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ confirmation: `PRONTO-${body.invoice_id}`, amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' }) });
  });

  await lookup(page);
  await expect(page.getByTestId('invoice-select')).toBeVisible();
  await expect(page.getByTestId('cart')).toContainText('Your cart (3)');
  await openCardReview(page);
  await expect(page.getByTestId('batch-review')).toBeVisible();
  await page.getByTestId('pay-submit').click();

  await expect(page.getByTestId('batch-result')).toBeVisible();
  await expect(page.getByTestId('batch-status-inv-1')).toContainText('Paid');
  await expect(page.getByTestId('batch-status-inv-2')).toContainText('Paid');
  await expect(page.getByTestId('batch-status-inv-3')).toContainText('Paid');
  expect(posts.map(entry => entry.invoiceId).sort()).toEqual(['inv-1', 'inv-2', 'inv-3']);
  const keys = posts.map(entry => entry.key);
  expect(new Set(keys).size).toBe(3);
  keys.forEach(key => expect(key).toMatch(/^[0-9a-f-]{36}$/));
});

test('partial batch failure retries only the unpaid invoice with the same key', async ({ page }) => {
  const posts: Array<{ invoiceId: string; key: string }> = [];
  let failNext = true;
  await mockConfig(page);
  await mockInvoices(page);
  await page.route(/\/payers(\?|\/|$)/, route => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  await mockQuotes(page);
  await page.route(/\/payments(\?|$)/, route => {
    const body = route.request().postDataJSON() as { invoice_id: string };
    posts.push({ invoiceId: body.invoice_id, key: route.request().headers()['idempotency-key'] ?? '' });
    if (body.invoice_id === 'inv-2' && failNext) {
      failNext = false;
      return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'processor unavailable' }) });
    }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ confirmation: `PRONTO-${body.invoice_id}`, amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' }) });
  });

  await lookup(page);
  await openCardReview(page);
  await page.getByTestId('pay-submit').click();

  await expect(page.getByTestId('batch-status-inv-2')).toContainText('Not charged');
  await expect(page.getByTestId('error')).toContainText('could not be completed');
  expect(posts).toHaveLength(3);

  await page.getByTestId('retry-batch').click();
  await expect(page.getByTestId('batch-status-inv-2')).toContainText('Paid');

  // Only inv-2 is retried; the other two are never re-posted (no double-charge).
  expect(posts).toHaveLength(4);
  const inv2Posts = posts.filter(entry => entry.invoiceId === 'inv-2');
  expect(inv2Posts).toHaveLength(2);
  expect(inv2Posts[0].key).toBe(inv2Posts[1].key);
});

function requiredEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} must be set by the pipeline.`);
  return value;
}
