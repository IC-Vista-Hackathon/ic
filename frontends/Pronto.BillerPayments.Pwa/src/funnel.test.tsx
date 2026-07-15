// @vitest-environment jsdom
// Proves the payer funnel actually emits its analytics events (not just session_started) and
// that every emitted event + its dimensions survive the strict allowlist. This is the
// deterministic counterpart to the nonprod browser smoke, which only asserts session_started.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { sanitizeEvent } from './telemetryPolicy';
import type { Invoice, PayerProfile, PaymentReceipt } from './types';

const trackEvent = vi.fn<(name: string, properties?: Record<string, unknown>) => void>();
vi.mock('./insights', () => ({ trackEvent, initBrowserTelemetry: vi.fn(), flowId: () => undefined }));

const pay = vi.fn<() => Promise<PaymentReceipt>>();
vi.mock('./provider', () => ({
  ServicePaymentExperienceProvider: class {
    quote = vi.fn().mockResolvedValue({ feeCents: 0, totalCents: 5000 });
    getInvoices = vi.fn().mockResolvedValue([invoice]);
    findPayer = vi.fn().mockResolvedValue(payer);
    getPayments = vi.fn().mockResolvedValue([]);
    updatePreferences = vi.fn();
    askAssistant = vi.fn().mockResolvedValue({ reply: 'ACH is cheaper.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.' });
    pay = pay;
  },
}));

const invoice: Invoice = { id: 'inv-1', accountNumber: '4421', payerName: 'Pat', amountCents: 5000, dueDate: '2026-08-01', description: 'Water bill', status: 'due' };
const payer: PayerProfile = { payer_id: 'payer-1', biller_id: 'biller-1', name: 'Pat', email: 'pat@example.com', account_numbers: ['4421'], preferences: { autopay: false, paperless: false, channels: ['email'], payment_day: null } };
const receipt: PaymentReceipt = { confirmation: 'CONF-123', amountCents: 5000, feeCents: 0, totalCents: 5000, status: 'succeeded', payerAccountId: 'payer-1' };
const config = {
  schema_version: '1.0',
  biller_id: 'biller-1',
  brand: { display_name: 'Acme Water', primary_color: '#123456', secondary_color: '#654321', font_family: null },
  content: { heading: 'Pay your bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Acme Water Payments', short_name: 'Acme Water', theme_color: '#123456', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};

const names = () => trackEvent.mock.calls.map(([name]) => name);
function assertAllAllowlisted() {
  for (const [name, properties = {}] of trackEvent.mock.calls) {
    expect(sanitizeEvent(name, properties as never), `event ${name} rejected by allowlist`).not.toBeNull();
  }
}

describe('payer funnel telemetry', () => {
  beforeEach(() => { pay.mockResolvedValue(receipt); vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(config), { status: 200 }))); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.unstubAllGlobals(); });

  it('emits allowlisted lookup, method, review, submit and completed events', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await user.click(await screen.findByTestId('method-ach'));
    await user.click(await screen.findByTestId('review-submit'));
    await user.click(await screen.findByTestId('pay-submit'));
    await screen.findByTestId('payment-confirmation');

    expect(names()).toEqual(expect.arrayContaining([
      'pwa.bill_lookup', 'pwa.payment_method_selected', 'pwa.review_opened',
      'pwa.payment_submitted', 'pwa.payment_completed',
    ]));
    expect(trackEvent).toHaveBeenCalledWith('pwa.bill_lookup', expect.objectContaining({ outcome: 'found' }));
    assertAllAllowlisted();
  });

  it('emits an allowlisted payment_failed event when the payment mutation throws', async () => {
    pay.mockRejectedValueOnce(new Error('gateway declined'));
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await user.click(await screen.findByTestId('review-submit'));
    await user.click(await screen.findByTestId('pay-submit'));
    await screen.findByTestId('error');

    expect(names()).toContain('pwa.payment_failed');
    expect(trackEvent).toHaveBeenCalledWith('pwa.payment_failed', expect.objectContaining({ error_category: expect.any(String) }));
    assertAllAllowlisted();
  });
});
