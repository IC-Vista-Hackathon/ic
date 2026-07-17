// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Invoice, PayerProfile, PaymentHistory, PaymentReceipt, PaymentRequest } from './types';

const payCalls: PaymentRequest[] = [];
const pay = vi.fn<(request: PaymentRequest) => Promise<PaymentReceipt>>();
const getPayments = vi.fn<() => Promise<PaymentHistory[]>>();
const findPayer = vi.fn<() => Promise<PayerProfile | undefined>>();

vi.mock('./provider', () => ({
  ServicePaymentExperienceProvider: class {
    quote = vi.fn(async (invoiceId: string, method: string) => ({ feeCents: method === 'card' ? 250 : 0, totalCents: 250 }));
    getInvoices = vi.fn().mockResolvedValue(invoices);
    findPayer = findPayer;
    getPayments = getPayments;
    updatePreferences = vi.fn();
    pay = pay;
  },
}));

const invoices: Invoice[] = [
  { id: 'inv-1', accountNumber: '4421', payerName: 'Pat', amountCents: 12500, dueDate: '2026-08-01', description: 'Water service', status: 'due', type: 'Water' },
  { id: 'inv-2', accountNumber: '4421', payerName: 'Pat', amountCents: 8000, dueDate: '2026-08-01', description: 'Sewer service', status: 'due', type: 'Sewer' },
  { id: 'inv-3', accountNumber: '4421', payerName: 'Pat', amountCents: 4500, dueDate: '2026-08-01', description: 'Trash service', status: 'due', type: 'Trash' },
];

const config = {
  schema_version: '1.0',
  biller_id: 'biller-1',
  brand: { display_name: 'Acme Water', primary_color: '#123456', secondary_color: '#654321', font_family: null },
  content: { heading: 'Pay your bill', introduction: 'Fast and secure.', support_text: 'Need help?', privacy_policy_url: '', terms_of_service_url: '' },
  pwa: { name: 'Acme Water Payments', short_name: 'Acme Water', theme_color: '#123456', background_color: '#ffffff' },
  enabled_payment_capabilities: ['card', 'ach'],
};

function receiptFor(invoiceId: string): PaymentReceipt {
  return { confirmation: `CONF-${invoiceId}`, amountCents: 1, feeCents: 0, totalCents: 1, status: 'succeeded' };
}

async function reachBatchReview(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByTestId('lookup-submit'));
  await screen.findByTestId('invoice-select');
  await waitFor(() => expect((screen.getByTestId('review-submit') as HTMLButtonElement).disabled).toBe(false));
  await user.click(screen.getByTestId('review-submit'));
  await screen.findByTestId('batch-review');
}

describe('multi-invoice cart + batch checkout', () => {
  beforeEach(() => {
    payCalls.length = 0;
    findPayer.mockResolvedValue(undefined);
    getPayments.mockResolvedValue([]);
    pay.mockImplementation(async (request: PaymentRequest) => { payCalls.push(request); return receiptFor(request.invoiceId); });
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(config), { status: 200 })));
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.unstubAllGlobals(); });

  it('renders a selectable list + cart with all open invoices selected by default', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));

    const list = await screen.findByTestId('invoice-select');
    expect(within(list).getByTestId('invoice-option-inv-1')).toBeDefined();
    expect(within(list).getByTestId('invoice-option-inv-3')).toBeDefined();
    // Cart subtotal aggregates every selected invoice's amount (125 + 80 + 45 = $250.00).
    expect(screen.getByTestId('cart').textContent).toContain('Your cart (3)');
    expect(screen.getByTestId('cart-subtotal').textContent).toBe('$250.00');
  });

  it('deselecting an invoice updates the cart total', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('invoice-select');

    const option = within(screen.getByTestId('invoice-option-inv-3')).getByRole('checkbox');
    await user.click(option);

    expect(screen.getByTestId('cart').textContent).toContain('Your cart (2)');
    await waitFor(() => expect(screen.getByTestId('cart-subtotal').textContent).toBe('$205.00'));
    expect(screen.queryByTestId('cart-line-inv-3')).toBeNull();
  });

  it('settles each selected invoice with one POST and a distinct idempotency key', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await reachBatchReview(user);
    await user.click(screen.getByTestId('pay-submit'));

    await waitFor(() => expect(screen.getByTestId('batch-result')).toBeDefined());
    expect(pay).toHaveBeenCalledTimes(3);
    expect(payCalls.map(call => call.invoiceId).sort()).toEqual(['inv-1', 'inv-2', 'inv-3']);
    const keys = payCalls.map(call => call.idempotencyKey);
    expect(new Set(keys).size).toBe(3);
    // Client never sends money fields; amount/fee/total stay server-authoritative.
    payCalls.forEach(call => {
      expect(call).not.toHaveProperty('amountCents');
      expect(call).not.toHaveProperty('feeCents');
      expect(call).not.toHaveProperty('totalCents');
    });
    expect(screen.getByTestId('batch-status-inv-2').textContent).toContain('Paid');
  });

  it('surfaces partial failure and retries only the unpaid invoice with the same key', async () => {
    let failInv2 = true;
    pay.mockImplementation(async (request: PaymentRequest) => {
      payCalls.push(request);
      if (request.invoiceId === 'inv-2' && failInv2) { failInv2 = false; throw new Error('processor unavailable'); }
      return receiptFor(request.invoiceId);
    });
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await reachBatchReview(user);
    await user.click(screen.getByTestId('pay-submit'));

    await waitFor(() => expect(screen.getByTestId('error').textContent).toContain('could not be completed'));
    expect(screen.getByTestId('batch-status-inv-2').textContent).toContain('Not charged');
    expect(payCalls).toHaveLength(3);

    await user.click(screen.getByTestId('retry-batch'));
    await waitFor(() => expect(screen.getByTestId('batch-status-inv-2').textContent).toContain('Paid'));

    // Only inv-2 is retried; inv-1 and inv-3 are never charged twice.
    expect(payCalls).toHaveLength(4);
    const inv2 = payCalls.filter(call => call.invoiceId === 'inv-2');
    expect(inv2).toHaveLength(2);
    expect(inv2[0].idempotencyKey).toBe(inv2[1].idempotencyKey);
    expect(payCalls.filter(call => call.invoiceId === 'inv-1')).toHaveLength(1);
  });
});
