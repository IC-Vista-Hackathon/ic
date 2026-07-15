// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Invoice, PayerProfile, PaymentHistory, PaymentReceipt } from './types';

const pay = vi.fn<() => Promise<PaymentReceipt>>();
const getPayments = vi.fn<() => Promise<PaymentHistory[]>>();
const findPayer = vi.fn<() => Promise<PayerProfile | undefined>>();

vi.mock('./provider', () => ({
  ServicePaymentExperienceProvider: class {
    quote = vi.fn().mockResolvedValue({ feeCents: 0, totalCents: 5000 });
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

const config = {
  schema_version: '1.0',
  biller_id: 'biller-1',
  brand: { display_name: 'Acme Water', primary_color: '#123456', secondary_color: '#654321', font_family: null },
  content: { heading: 'Pay your bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Acme Water Payments', short_name: 'Acme Water', theme_color: '#123456', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};

async function reachReview(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByTestId('lookup-submit'));
  await user.click(await screen.findByTestId('review-submit'));
  await screen.findByTestId('pay-submit');
}

describe('payment success followed by history refresh failure', () => {
  beforeEach(() => {
    findPayer.mockResolvedValue(payer);
    getPayments.mockResolvedValue([]);
    pay.mockResolvedValue(receipt);
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(config), { status: 200 })));
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.unstubAllGlobals(); });

  it('shows confirmation when the post-payment history refresh throws', async () => {
    getPayments.mockReset();
    getPayments.mockResolvedValueOnce([]); // lookup refresh succeeds
    getPayments.mockRejectedValueOnce(new Error('history unavailable')); // post-payment refresh throws
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await reachReview(user);

    await user.click(screen.getByTestId('pay-submit'));

    await waitFor(() => expect(screen.getByTestId('payment-confirmation')).toBeDefined());
    expect(screen.getByTestId('confirmation-code').textContent).toBe('CONF-123');
    expect(screen.queryByTestId('error')).toBeNull();
    expect(pay).toHaveBeenCalledTimes(1);
  });

  it('shows failure only when the payment mutation itself throws', async () => {
    pay.mockRejectedValueOnce(new Error('gateway declined'));
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await reachReview(user);

    await user.click(screen.getByTestId('pay-submit'));

    await waitFor(() => expect(screen.getByTestId('error')).toBeDefined());
    expect(screen.queryByTestId('payment-confirmation')).toBeNull();
  });
});
