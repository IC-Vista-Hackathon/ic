// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { Invoice, PayerProfile, PaymentHistory, PaymentReceipt, PaymentRequest } from './types';

const pay = vi.fn<(request: PaymentRequest) => Promise<PaymentReceipt>>();
const getPayments = vi.fn<() => Promise<PaymentHistory[]>>();
const findPayer = vi.fn<() => Promise<PayerProfile | undefined>>();
const askAssistant = vi.fn();
const chat = vi.fn();

vi.mock('./provider', () => ({
  ServicePaymentExperienceProvider: class {
    quote = vi.fn().mockResolvedValue({ feeCents: 0, totalCents: 5000 });
    getInvoices = vi.fn().mockResolvedValue([invoice]);
    findPayer = findPayer;
    getPayments = getPayments;
    updatePreferences = vi.fn();
    askAssistant = askAssistant;
    chat = chat;
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
    askAssistant.mockResolvedValue({ reply: 'ACH is cheaper.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.' });
    chat.mockResolvedValue({ reply: 'The ACH fee is $1.50.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.' });
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

describe('payment assistant surface', () => {
  beforeEach(() => {
    // The assistant is opt-in (ASSISTANT_ENABLED reads import.meta.env at module load), so enable
    // the flag and re-evaluate App before each test in this block.
    vi.stubEnv('VITE_PAYER_ASSISTANT', 'true');
    vi.resetModules();
    findPayer.mockResolvedValue(payer);
    getPayments.mockResolvedValue([]);
    pay.mockResolvedValue(receipt);
    askAssistant.mockResolvedValue({ reply: 'ACH is cheaper.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.' });
    chat.mockResolvedValue({ reply: 'The ACH fee is $1.50.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.' });
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(config), { status: 200 })));
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.unstubAllGlobals(); vi.unstubAllEnvs(); vi.resetModules(); });

  it('renders the recommendation after lookup and applies it to the method selection', async () => {
    askAssistant.mockResolvedValue({ reply: 'ACH beats card on this bill.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH beats card on this bill.' });
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));

    expect((await screen.findByTestId('assistant-transcript')).textContent).toContain('ACH beats card');
    expect(askAssistant).toHaveBeenCalledWith('inv-1', '4421');

    await user.click(await screen.findByTestId('assistant-apply'));
    expect(screen.getByTestId('method-ach').className).toContain('selected');
  });

  it('answers a follow-up question in the conversation', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('assistant-transcript');

    await user.type(await screen.findByTestId('assistant-input'), 'what is the fee?');
    await user.click(await screen.findByTestId('assistant-send'));

    expect(await screen.findByText('The ACH fee is $1.50.')).toBeDefined();
    expect(chat).toHaveBeenCalledWith('inv-1', '4421', [
      { role: 'assistant', content: 'ACH is cheaper.' },
      { role: 'user', content: 'what is the fee?' },
    ]);
  });

  it('offers an in-chat confirm control on pay intent and pays through it', async () => {
    chat.mockResolvedValue({ reply: "You can confirm right here.", method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.', action: { kind: 'confirm_payment', method: 'ach', totalCents: 5150 } });
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('assistant-transcript');

    await user.type(await screen.findByTestId('assistant-input'), "let's pay it now");
    await user.click(await screen.findByTestId('assistant-send'));

    // The confirm control appears; tapping it is the explicit confirmation that submits.
    await user.click(await screen.findByTestId('assistant-confirm'));

    await waitFor(() => expect(screen.getByTestId('payment-confirmation')).toBeDefined());
    expect(pay).toHaveBeenCalledTimes(1);
    expect(pay.mock.calls[0][0].method).toBe('ach');
  });

  it('in-chat confirm pays the full balance even when a partial plan is selected', async () => {
    // A biller that allows installments unlocks the partial/installment journey below the assistant.
    const partialConfig = { ...config, billing: { categories: [{ id: 'c1', display_name: 'Water', cadence_label: 'Monthly', state_summary: 'Active', payment_mode: 'installments_allowed', maximum_installments: 4 }] } };
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(partialConfig), { status: 200 })));
    chat.mockResolvedValue({ reply: 'You can confirm right here.', method: 'ach', scheduledFor: '2026-08-01', feeCents: 150, totalCents: 5150, rationale: 'ACH is cheaper.', action: { kind: 'confirm_payment', method: 'ach', totalCents: 5150 } });
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('assistant-transcript');

    // Payer selects a partial plan and enters a smaller amount, then confirms the assistant's
    // full-balance total from chat — the charge must be the full balance, not the partial amount.
    await user.click(await screen.findByTestId('plan-mode-partial'));
    await user.type(await screen.findByTestId('partial-amount-input'), '10.00');
    await user.type(await screen.findByTestId('assistant-input'), "let's pay it now");
    await user.click(await screen.findByTestId('assistant-send'));
    await user.click(await screen.findByTestId('assistant-confirm'));

    await waitFor(() => expect(pay).toHaveBeenCalledTimes(1));
    const request = pay.mock.calls[0][0];
    expect(request.amountCents).toBeUndefined();
    expect(request.installmentCount).toBeUndefined();
  });

  it('shows no confirm control for a plain question', async () => {
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));
    await screen.findByTestId('assistant-transcript');

    await user.type(await screen.findByTestId('assistant-input'), 'what is the fee?');
    await user.click(await screen.findByTestId('assistant-send'));

    await screen.findByText('The ACH fee is $1.50.');
    expect(screen.queryByTestId('assistant-confirm')).toBeNull();
  });

  it('keeps the manual flow usable when the assistant fails', async () => {
    askAssistant.mockRejectedValue(new Error('assistant down'));
    const { App } = await import('./App');
    const user = userEvent.setup();
    render(<App />);
    await user.click(await screen.findByTestId('lookup-submit'));

    // Assistant errors, but the bill + method choices still render so the payer can proceed.
    expect(await screen.findByTestId('method-ach')).toBeDefined();
    expect(screen.queryByTestId('assistant-apply')).toBeNull();
  });
});
