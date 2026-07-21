import { expect, test, type Page } from '@playwright/test';
import { installHarness, DEFAULT_ACCOUNT_NUMBER } from './harness';

// Whole-app UI journey for the shipped payer PWA, driven in a real browser against a faked backend.
// Covers the exact path a payer walks: boot → find bill → assistant recommends → choose method →
// review → pay → confirmation. If the bundle white-screens, a button dies, or the assistant leg
// breaks, this fails — the kind of regression the provider-mocking unit tests cannot see.

async function findBill(page: Page): Promise<void> {
  await page.goto('/pay/');
  await expect(page.getByRole('heading', { name: 'Pay your water bill' })).toBeVisible();
  await page.getByTestId('account-input').fill(DEFAULT_ACCOUNT_NUMBER);
  await page.getByTestId('lookup-submit').click();
  await expect(page.getByTestId('method-card')).toBeVisible();
}

test('payer finds a bill, sees an assistant recommendation, and pays end to end', async ({ page }) => {
  await installHarness(page);

  await findBill(page);

  // The opt-in assistant auto-runs a payer-chat turn once a bill is in hand and renders its reply.
  await expect(page.getByTestId('assistant')).toBeVisible();
  await expect(page.getByTestId('assistant-assistant').first()).toContainText('card');

  await page.getByTestId('method-card').click();
  await expect(page.getByTestId('review-submit')).toBeEnabled();
  await page.getByTestId('review-submit').click();

  await expect(page.getByTestId('pay-submit')).toBeEnabled();
  await page.getByTestId('pay-submit').click();

  await expect(page.getByTestId('payment-confirmation')).toBeVisible();
  await expect(page.getByTestId('confirmation-code')).toHaveText('PRONTO-ABC123');
});

test('the assistant answers a follow-up question in the transcript', async ({ page }) => {
  await installHarness(page, {
    assistantResponse: {
      reply: 'A bank transfer has no fee, but it takes 2-3 business days to clear.',
      artifacts: { payment_plan: { method: 'ach', when: 'now', fee_cents: 0, total_cents: 12500, rationale: 'no fee' } },
    },
  });

  await findBill(page);
  await expect(page.getByTestId('assistant')).toBeVisible();

  await page.getByTestId('assistant-input').fill('Is paying by bank cheaper?');
  await page.getByTestId('assistant-send').click();

  await expect(page.getByTestId('assistant-user').last()).toContainText('Is paying by bank cheaper?');
  await expect(page.getByTestId('assistant-assistant').last()).toContainText('bank transfer has no fee');
});

test('a slow assistant turn still resolves without a timeout error', async ({ page }) => {
  // The payer-chat turn runs a server-side agent pipeline; the reply lands slower than the generic
  // 15s request budget and must still resolve on the longer assistant budget. A regression back to
  // the 15s default would abort here — so the delay is deliberately above 15s, mirroring the Studio
  // slow-publish test (guards the same class of bug as the publish timeout, at the UI boundary).
  await installHarness(page, { assistantDelayMs: 16_000 });

  await findBill(page);

  await expect(page.getByTestId('assistant-assistant').first()).toContainText('card', { timeout: 25_000 });
  await expect(page.getByTestId('error')).toHaveCount(0);
});
