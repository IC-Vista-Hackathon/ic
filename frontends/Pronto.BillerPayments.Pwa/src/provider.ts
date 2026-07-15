import type { Invoice, PayerProfile, PaymentHistory, PaymentReceipt, PaymentRequest } from './types';
import { logError, observed, sharedFlow, traceHeaders } from './telemetry';
import { fetchWithTimeout, requestError } from './http';

export interface PaymentQuote { feeCents: number; totalCents: number; }

export interface PaymentExperienceProvider {
  getInvoices(accountNumber: string): Promise<Invoice[]>;
  findPayer(accountNumber: string): Promise<PayerProfile | undefined>;
  updatePreferences(payerId: string, preferences: PayerProfile['preferences']): Promise<PayerProfile['preferences']>;
  getPayments(payerId: string): Promise<PaymentHistory[]>;
  quote(invoiceId: string, method: string): Promise<PaymentQuote>;
  pay(request: PaymentRequest): Promise<PaymentReceipt>;
}

export class ServicePaymentExperienceProvider implements PaymentExperienceProvider {
  // Shared with browser telemetry so events and API requests correlate on one trace id.
  private readonly flow = sharedFlow;
  constructor(private readonly billerId: string) {}
  private headers(json = false) { return { ...(json ? { 'content-type': 'application/json' } : {}), ...traceHeaders(this.flow, this.billerId) }; }

  getInvoices(accountNumber: string) { return observed('pwa.invoice.lookup', async () => {
    const response = await fetchWithTimeout(`/invoices/billers/${encodeURIComponent(this.billerId)}/invoices?account_number=${encodeURIComponent(accountNumber)}&include_closed=true`, { headers: this.headers() });
    const payload = await read<{ invoices: Array<{ id: string; account_number: string; payer_name: string; amount_cents: number; due_date: string; description: string; status: string; type?: string | null; status_color?: string | null; note?: string | null; note_emphasis?: boolean }> }>(response);
    return payload.invoices.map(invoice => ({ id: invoice.id, accountNumber: invoice.account_number, payerName: invoice.payer_name, amountCents: invoice.amount_cents, dueDate: invoice.due_date, description: invoice.description, status: invoice.status, type: invoice.type ?? undefined, statusColor: invoice.status_color ?? undefined, note: invoice.note ?? undefined, noteEmphasis: invoice.note_emphasis ?? false }));
  }); }

  // Server-side quote: the same fee policy the payment itself will apply — never computed client-side.
  quote(invoiceId: string, method: string) { return observed('pwa.payment.quote', async () => {
    const payload = await read<{ fee_cents: number; total_cents: number }>(await fetchWithTimeout(`/payments/quote?biller_id=${encodeURIComponent(this.billerId)}&invoice_id=${encodeURIComponent(invoiceId)}&method=${encodeURIComponent(method)}`, { headers: this.headers() }));
    return { feeCents: payload.fee_cents, totalCents: payload.total_cents };
  }); }

  async findPayer(accountNumber: string) {
    const response = await fetchWithTimeout(`/payers?biller_id=${encodeURIComponent(this.billerId)}&account_number=${encodeURIComponent(accountNumber)}`, { headers: this.headers() });
    if (response.status === 404) return undefined;
    return read<PayerProfile>(response);
  }

  updatePreferences(payerId: string, preferences: PayerProfile['preferences']) { return observed('pwa.preferences.update', async () =>
    read<PayerProfile['preferences']>(await fetchWithTimeout(`/payers/${encodeURIComponent(payerId)}/preferences?biller_id=${encodeURIComponent(this.billerId)}`, { method: 'PATCH', headers: this.headers(true), body: JSON.stringify(preferences) })));
  }

  getPayments(payerId: string) { return observed('pwa.payments.history', async () =>
    read<PaymentHistory[]>(await fetchWithTimeout(`/payments?biller_id=${encodeURIComponent(this.billerId)}&payer_account_id=${encodeURIComponent(payerId)}`, { headers: this.headers() })));
  }

  pay(request: PaymentRequest) { return observed('pwa.payment.submit', async () => {
    let payer = await this.findPayer(request.accountNumber);
    const payment = await read<{ confirmation: string; amount_cents: number; fee_cents: number; total_cents: number; status: string; scheduled_for?: string }>(await fetchWithTimeout('/payments', {
      method: 'POST',
      headers: { ...this.headers(true), 'idempotency-key': request.idempotencyKey },
      body: JSON.stringify({ biller_id: this.billerId, invoice_id: request.invoiceId, method: request.method, payer_account_id: payer?.payer_id, scheduled_for: request.scheduledFor }),
    }));
    let preferenceUpdateFailed = false;
    if (request.autoPay || request.paperless) {
      const preferences = { autopay: request.autoPay, paperless: request.paperless, channels: ['email'] as Array<'email'|'sms'>, payment_day: request.autoPay ? 15 : null };
      try {
        if (payer) {
          payer = { ...payer, preferences: await this.updatePreferences(payer.payer_id, preferences) };
        } else {
          payer = await read<PayerProfile>(await fetchWithTimeout('/payers', { method: 'POST', headers: this.headers(true), body: JSON.stringify({ biller_id: this.billerId, name: request.payerName, email: request.payerEmail, phone: null, account_numbers: [request.accountNumber], preferences }) }));
        }
      } catch (caught) {
        preferenceUpdateFailed = true;
        logError('pwa.preferences.save_failed', caught);
      }
    }
    return { confirmation: payment.confirmation, amountCents: payment.amount_cents, feeCents: payment.fee_cents, totalCents: payment.total_cents, status: payment.status, scheduledFor: payment.scheduled_for, payerAccountId: payer?.payer_id, preferenceUpdateFailed };
  }); }
}

async function read<T>(response: Response): Promise<T> { if (!response.ok) throw await requestError(response, `Request failed with ${response.status}.`); return await response.json() as T; }
