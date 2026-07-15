import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';

const url = process.env.PAYER_URL;
const configPath = process.env.PAYER_CONFIG;
if (!url || !configPath) throw new Error('PAYER_URL and PAYER_CONFIG must be set by the pipeline.');

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

// Core regression gate. Runs against a bespoke generated bundle but only touches the
// stable core via fixed data-testids, so the same script passes for every skin.
test('payer completes lookup -> method -> review -> pay', async ({ page }) => {
  await page.route(/\/api\/public\/experiences\//, route =>
    route.request().url().includes('manifest')
      ? route.fulfill({ contentType: 'application/manifest+json', body: '{}' })
      : route.fulfill({ contentType: 'application/json', body: JSON.stringify(definition) }),
  );
  await page.route(/\/invoices\//, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ invoices: [invoice] }) }),
  );
  await page.route(/\/payers(\?|\/|$)/, route => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  // Server-side quote drives the enabled pay button; mock it before the generic /payments route.
  await page.route(/\/payments\/quote/, route =>
    route.fulfill({ contentType: 'application/json', body: JSON.stringify({ fee_cents: 250, total_cents: 12750 }) }),
  );
  await page.route(/\/payments(\?|$)/, route =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ confirmation: 'PRONTO-ABC123', amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' }),
    }),
  );

  await page.goto(url);

  await expect(page.getByTestId('account-input')).toBeVisible();
  await page.getByTestId('account-input').fill('4421');
  await page.getByTestId('lookup-submit').click();

  await expect(page.getByTestId('method-card')).toBeVisible();
  await page.getByTestId('method-card').click();
  await page.getByTestId('review-submit').click();

  await expect(page.getByTestId('pay-submit')).toBeVisible();
  await page.getByTestId('pay-submit').click();

  await expect(page.getByTestId('payment-confirmation')).toBeVisible();
  await expect(page.getByTestId('confirmation-code')).toHaveText('PRONTO-ABC123');
});
