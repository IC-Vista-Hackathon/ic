// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Invoice, PayerProfile, PaymentHistory, PaymentReceipt, PaymentRequest } from './types';

const pay = vi.fn<(request: PaymentRequest) => Promise<PaymentReceipt>>();
const getPayments = vi.fn<() => Promise<PaymentHistory[]>>();
const findPayer = vi.fn<() => Promise<PayerProfile | undefined>>();
// The quote echoes the requested amount (full balance when none is given) so the core can price
// a partial payment exactly the way the server will.
const quote = vi.fn(async (_invoiceId: string, _method: string, amountCents?: number) => {
  const amount = amountCents ?? 5000;
  return { feeCents: 0, totalCents: amount, amountCents: amount, outstandingCents: 5000 };
});

vi.mock('./provider', () => ({
  ServicePaymentExperienceProvider: class {
    quote = quote;
    getInvoices = vi.fn().mockResolvedValue([invoice]);
    findPayer = findPayer;
    getPayments = getPayments;
    updatePreferences = vi.fn();
    pay = pay;
  },
}));

const invoice: Invoice = {
  id: 'inv-1', accountNumber: '4421', payerName: 'Pat', amountCents: 5000,
  dueDate: '2026-08-01', description: 'Water bill', status: 'due',
};
const payer: PayerProfile = {
  payer_id: 'payer-1', biller_id: 'biller-1', name: 'Pat', email: 'pat@example.com',
  account_numbers: ['4421'], preferences: { autopay: false, paperless: false, channels: ['email'], payment_day: null },
};
const receipt: PaymentReceipt = {
  confirmation: 'CONF-123', amountCents: 5000, feeCents: 0, totalCents: 5000, status: 'succeeded', payerAccountId: 'payer-1',
};

const baseConfig = {
  schema_version: '1.0',
  biller_id: 'biller-1',
  brand: { display_name: 'Acme Water', primary_color: '#123456', secondary_color: '#654321', font_family: null },
  content: { heading: 'Pay your bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Acme Water Payments', short_name: 'Acme Water', theme_color: '#123456', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};
// A biller whose policy permits installments unlocks the partial/installment journey.
const installmentConfig = {
  ...baseConfig,
  billing: { categories: [{ id: 'c1', display_name: 'Water', cadence_label: 'Monthly', state_summary: 'Due', payment_mode: 'installments_allowed', maximum_installments: 12 }] },
};

function stubConfig(config: object) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(config), { status: 200 })));
}

async function renderAndLookup() {
  const { App } = await import('./App');
  const user = userEvent.setup();
  render(<App />);
  await user.click(await screen.findByTestId('lookup-submit'));
  await screen.findByTestId('review-submit');
  return user;
}

describe('F4 partial-payment / installment authorable journey', () => {
  beforeEach(() => {
    findPayer.mockResolvedValue(payer);
    getPayments.mockResolvedValue([]);
    pay.mockResolvedValue(receipt);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.unstubAllGlobals(); });

  it('hides the plan chooser for an ineligible biller and keeps the full-payment default', async () => {
    stubConfig(baseConfig);
    const user = await renderAndLookup();

    expect(screen.queryByTestId('plan-chooser')).toBeNull();

    await user.click(screen.getByTestId('review-submit'));
    await user.click(await screen.findByTestId('pay-submit'));
    await waitFor(() => expect(pay).toHaveBeenCalledTimes(1));
    const request = pay.mock.calls[0][0];
    expect(request.amountCents).toBeUndefined();
    expect(request.installmentCount).toBeUndefined();
  });

  it('shows the plan chooser for an eligible biller', async () => {
    stubConfig(installmentConfig);
    await renderAndLookup();
    expect(await screen.findByTestId('plan-chooser')).toBeDefined();
    expect(screen.getByTestId('plan-mode-partial')).toBeDefined();
    expect(screen.getByTestId('plan-mode-installment')).toBeDefined();
  });

  it('validates the entered amount against the balance and blocks review', async () => {
    stubConfig(installmentConfig);
    const user = await renderAndLookup();
    await user.click(await screen.findByTestId('plan-mode-partial'));

    await user.type(screen.getByTestId('partial-amount-input'), '9999');
    expect(await screen.findByTestId('plan-amount-error')).toBeDefined();
    expect((screen.getByTestId('review-submit') as HTMLButtonElement).disabled).toBe(true);
  });

  it('flows a valid partial amount through pay() as amountCents', async () => {
    stubConfig(installmentConfig);
    const user = await renderAndLookup();
    await user.click(await screen.findByTestId('plan-mode-partial'));
    await user.type(screen.getByTestId('partial-amount-input'), '20.00');

    await waitFor(() => expect((screen.getByTestId('review-submit') as HTMLButtonElement).disabled).toBe(false));
    await user.click(screen.getByTestId('review-submit'));
    await user.click(await screen.findByTestId('pay-submit'));

    await waitFor(() => expect(pay).toHaveBeenCalledTimes(1));
    expect(pay.mock.calls[0][0].amountCents).toBe(2000);
    expect(pay.mock.calls[0][0].installmentCount).toBeUndefined();
  });

  it('re-quotes the partial amount server-side when the payment method changes', async () => {
    stubConfig(installmentConfig);
    const user = await renderAndLookup();
    await user.click(await screen.findByTestId('plan-mode-partial'));
    await user.type(screen.getByTestId('partial-amount-input'), '20.00');

    // Partial amount is priced for the initially-selected card method.
    await waitFor(() => expect(quote).toHaveBeenCalledWith('inv-1', 'card', 2000));

    // Switching method must re-price the same amount so the review total matches the new fee.
    await user.click(screen.getByTestId('method-ach'));
    await waitFor(() => expect(quote).toHaveBeenCalledWith('inv-1', 'ach', 2000));
  });

  it('flows an installment-plan selection through pay() as installmentCount', async () => {
    stubConfig(installmentConfig);
    const user = await renderAndLookup();
    await user.click(await screen.findByTestId('plan-mode-installment'));
    await user.click(await screen.findByTestId('installment-option-3'));

    await user.click(screen.getByTestId('review-submit'));
    await user.click(await screen.findByTestId('pay-submit'));

    await waitFor(() => expect(pay).toHaveBeenCalledTimes(1));
    expect(pay.mock.calls[0][0].installmentCount).toBe(3);
    expect(pay.mock.calls[0][0].amountCents).toBeUndefined();
  });
});
