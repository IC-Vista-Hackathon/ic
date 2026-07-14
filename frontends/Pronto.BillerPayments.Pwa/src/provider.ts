import type { Invoice, PaymentReceipt, PaymentRequest } from './types';
import { observed } from './telemetry';

export interface PaymentExperienceProvider { getInvoice(accountNumber: string): Promise<Invoice>; pay(request: PaymentRequest): Promise<PaymentReceipt> }

export class ServicePaymentExperienceProvider implements PaymentExperienceProvider {
  constructor(private readonly billerId: string) {}

  getInvoice(accountNumber: string) { return observed('pwa.invoice.lookup', async () => {
    const response = await fetch(`/invoices/billers/${encodeURIComponent(this.billerId)}/invoices?account_number=${encodeURIComponent(accountNumber)}`);
    const payload = await read<{ invoices: Array<{ id: string; account_number: string; amount_cents: number; due_date: string; description: string }> }>(response);
    const invoice = payload.invoices[0];
    if (!invoice) throw new Error('No open bill was found for that account.');
    return { id: invoice.id, accountNumber: invoice.account_number, amountCents: invoice.amount_cents, dueDate: invoice.due_date, description: invoice.description };
  }); }

  pay(request: PaymentRequest) { return observed('pwa.payment.submit', async () => {
    let payerAccountId: string | undefined;
    if (request.autoPay || request.paperless) {
      const payer = await read<{ payer_id: string }>(await fetch('/payers', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ biller_id: this.billerId, name: request.payerName, email: request.payerEmail, phone: null, account_numbers: [request.accountNumber], preferences: { autopay: request.autoPay, paperless: request.paperless, channels: ['email'], payment_day: request.autoPay ? 15 : null } }) }));
      payerAccountId = payer.payer_id;
    }
    const payment = await read<{ confirmation: string; amount_cents: number; fee_cents: number; status: string; scheduled_for?: string }>(await fetch('/payments', { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ biller_id: this.billerId, invoice_id: request.invoiceId, method: request.method, payer_account_id: payerAccountId, scheduled_for: request.scheduledFor }) }));
    return { confirmation: payment.confirmation, amountCents: payment.amount_cents, feeCents: payment.fee_cents, status: payment.status, scheduledFor: payment.scheduled_for };
  }); }
}

const jsonHeaders = { 'content-type': 'application/json' };
async function read<T>(response: Response): Promise<T> { const body = await response.json().catch(() => ({})); if (!response.ok) throw new Error(body.message ?? body.detail ?? `Request failed with ${response.status}.`); return body as T; }
