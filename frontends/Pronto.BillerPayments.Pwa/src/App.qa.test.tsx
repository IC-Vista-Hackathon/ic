// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Invoice, PayerProfile, PaymentHistory } from './types';

const getInvoices = vi.fn<() => Promise<Invoice[]>>();
const findPayer = vi.fn<() => Promise<PayerProfile | undefined>>();
const getPayments = vi.fn<() => Promise<PaymentHistory[]>>();
const updatePreferences = vi.fn();

vi.mock('./provider', () => ({
  ServicePaymentExperienceProvider: class {
    quote = vi.fn().mockResolvedValue({ feeCents: 0, totalCents: 5000 });
    getInvoices = getInvoices;
    findPayer = findPayer;
    getPayments = getPayments;
    updatePreferences = updatePreferences;
    pay = vi.fn();
  },
}));

const dueInvoice: Invoice = {
  id: 'inv-1', accountNumber: '4421', payerName: 'Pat', amountCents: 5000,
  dueDate: '2026-08-01', description: 'Water bill', status: 'due',
};
const paidInvoice: Invoice = {
  id: 'inv-0', accountNumber: '4421', payerName: 'Pat', amountCents: 4200,
  dueDate: '2026-07-01', description: 'Prior water bill', status: 'paid',
};
const payer: PayerProfile = {
  payer_id: 'payer-1', biller_id: 'biller-1', name: 'Pat', email: 'pat@example.com',
  account_numbers: ['4421'], preferences: { autopay: false, paperless: false, channels: ['email'], payment_day: null },
};
const config = {
  schema_version: '1.0',
  biller_id: 'biller-1',
  brand: { display_name: 'Acme Water', primary_color: '#123456', secondary_color: '#654321', font_family: null },
  content: { heading: 'Pay your bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Acme Water Payments', short_name: 'Acme Water', theme_color: '#123456', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};

describe('QA fixes', () => {
  beforeEach(() => {
    getInvoices.mockResolvedValue([paidInvoice, dueInvoice]);
    findPayer.mockResolvedValue(payer);
    getPayments.mockResolvedValue([]);
    updatePreferences.mockResolvedValue(payer.preferences);
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(config), { status: 200 })));
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.unstubAllGlobals(); });

  it('review consent copy matches the pay button label', async () => {
    getInvoices.mockResolvedValue([dueInvoice]);
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await user.click(await screen.findByTestId('review-submit'));

    const button = await screen.findByTestId('pay-submit');
    const label = button.textContent ?? '';
    expect(label).toContain('Pay $50.00');
    const consent = document.querySelector('.consent');
    expect(consent?.textContent).toContain(label);
  });

  it('shows a confirmation after saving preferences', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('review-submit');
    await user.click(screen.getByRole('button', { name: 'Preferences' }));
    await user.click(screen.getByRole('button', { name: 'Save Preferences' }));

    await waitFor(() => expect(screen.getByTestId('prefs-saved')).toBeDefined());
  });

  it('labels paid invoices as Paid in account history', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('review-submit');
    await user.click(screen.getByRole('button', { name: 'Account History' }));

    await screen.findByText('Prior water bill');
    expect(screen.getByText('Paid')).toBeDefined();
  });
});
