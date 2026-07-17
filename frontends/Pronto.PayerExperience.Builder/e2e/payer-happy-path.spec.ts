import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';
import { installHarness, mockConfig, mockInvoice, mockQuotes } from '../src/contract-gate/harness';

const url = requiredEnvironment('PAYER_URL');
const configPath = requiredEnvironment('PAYER_CONFIG');

const definition = JSON.parse(readFileSync(configPath, 'utf8'));
const payer = {
  payer_id: 'payer-1',
  biller_id: definition.biller_id,
  name: 'Test Payer',
  email: 'payer@example.com',
  account_numbers: ['4421'],
  preferences: { autopay: false, paperless: false, channels: ['email'], payment_day: null },
};

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

// UX / accessibility smoke: prove the generated experience's payment flow is reachable and
// operable driven PURELY by accessibility roles/labels (no data-testids), so it survives a
// widened, generated authorable structure. The payment-CONTRACT correctness assertions
// (idempotency key, exactly-once, real invoice id, no client amount/fee, confirmation
// matches the server) now live in the F6 runtime contract gate (src/contract-gate/).
test('payer completes the flow driven only by accessible roles (UX/a11y smoke)', async ({ page }) => {
  await installHarness(page, { definition });

  await page.goto(url);
  const main = page.getByRole('main');
  await main.getByRole('textbox', { name: /account number/i }).fill('4421');
  await main.getByRole('button', { name: 'Continue' }).click();
  await main.getByRole('button', { name: /^Card/ }).click();
  await main.getByRole('button', { name: 'Review Payment' }).click();
  await main.getByRole('button', { name: /^Pay/ }).click();

  await expect(page.getByRole('heading', { name: /Payment (received|scheduled)/ })).toBeVisible();
  await expect(page.getByText('PRONTO-ABC123')).toBeVisible();
});

test('quote failures stay on method selection and can be retried', async ({ page }) => {
  let quoteRequests = 0;
  await mockConfig(page, definition);
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
  await mockConfig(page, definition);
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
  await mockConfig(page, definition);
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

function requiredEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} must be set by the pipeline.`);
  return value;
}
