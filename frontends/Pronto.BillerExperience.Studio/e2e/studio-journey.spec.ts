import { expect, test, type Page } from '@playwright/test';
import { installHarness } from './harness';

// Walk the wizard up to the analyze step: pick a vertical, fill business details, choose a setup
// path. The Upload path needs no billing-category input, so it keeps the walk minimal while still
// satisfying every step's validation.
async function completeWizard(page: Page): Promise<void> {
  await page.goto('/studio/');
  await page.getByRole('button', { name: 'Try It Out' }).first().click();

  await expect(page.getByRole('heading', { name: 'What line of business is this for?' })).toBeVisible();
  await page.getByRole('button', { name: /Utilities/ }).click();
  await page.getByRole('button', { name: 'Continue' }).click();

  await expect(page.getByRole('heading', { name: 'Business Details' })).toBeVisible();
  await page.getByPlaceholder('e.g. Your Organization').fill('Springfield Water');
  await page.getByPlaceholder('Search states...').fill('California');
  await page.getByRole('button', { name: 'California' }).click();
  await page.getByRole('button', { name: /Upload biller data/ }).click();
  await page.getByRole('button', { name: 'Continue' }).click();

  await expect(page.getByRole('heading', { name: 'Brand Details' })).toBeVisible();
  await page.getByRole('button', { name: 'Build My Preview' }).click();
}

test('biller onboards, previews the real payer PWA in the iframe, and publishes end to end', async ({ page }) => {
  await installHarness(page);

  await completeWizard(page);

  // Analyze (create → chat → activity) resolves; move on to review, then open the preview.
  await page.getByRole('button', { name: 'Review agent findings' }).click();
  await expect(page.getByRole('heading', { name: "Let's Review Things" })).toBeVisible();
  await page.getByRole('button', { name: /Preview My Payment Site/ }).click();

  // The preview embeds the SHIPPED payer PWA against the seeded preview tenant. The regression was a
  // blank iframe, so assert the real payer UI renders inside the frame.
  await expect(page.getByTestId('preview-badge')).toBeVisible();
  const preview = page.frameLocator('[data-testid="preview-frame"]');
  await expect(preview.getByRole('heading', { name: 'Pay your water bill' })).toBeVisible({ timeout: 20_000 });
  await expect(preview.getByTestId('account-input')).toBeVisible();

  // Publish: opens the checkout modal, then runs update → approve → publish to a ready deployment.
  await page.getByRole('button', { name: /^Publish/ }).click();
  await expect(page.getByRole('dialog', { name: /Publish Springfield Water/ })).toBeVisible();
  await page.getByLabel('Card number').fill('4242 4242 4242 4242');
  await page.getByLabel('Expiration').fill('12/30');
  await page.getByLabel('Security code').fill('123');
  await page.getByRole('button', { name: /Pay & Publish/ }).click();

  // Success lands on the dashboard with the live banner.
  await expect(page.getByText('Your payer experience is live.')).toBeVisible({ timeout: 20_000 });
});

test('a slow compliance-gated publish still completes without a client timeout', async ({ page }) => {
  // The compliance gates (approve/publish) respond slower than the generic 15s request budget;
  // publish must still succeed because approve/publish use the longer compliance-gate budget. A
  // regression to the 15s default would abort here and fail the test at the browser boundary.
  await installHarness(page, { gateDelayMs: 16_000 });

  await completeWizard(page);

  await page.getByRole('button', { name: 'Review agent findings' }).click();
  await page.getByRole('button', { name: /Preview My Payment Site/ }).click();
  await expect(page.getByTestId('preview-badge')).toBeVisible();

  await page.getByRole('button', { name: /^Publish/ }).click();
  await page.getByLabel('Card number').fill('4242 4242 4242 4242');
  await page.getByLabel('Expiration').fill('12/30');
  await page.getByLabel('Security code').fill('123');
  await page.getByRole('button', { name: /Pay & Publish/ }).click();

  await expect(page.getByText('Your payer experience is live.')).toBeVisible({ timeout: 45_000 });
});
