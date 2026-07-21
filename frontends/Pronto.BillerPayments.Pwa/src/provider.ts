import type { Invoice, PayerProfile, PaymentHistory, PaymentReceipt, PaymentRequest } from './types';
import { logError, observed, sharedFlow, traceHeaders } from './telemetry';
import { fetchWithTimeout, requestError } from './http';

export interface PaymentQuote { feeCents: number; totalCents: number; amountCents: number; outstandingCents: number; }

// A confirmable payment the assistant surfaced because the payer expressed intent to pay. The
// payer's explicit tap on the confirm control is still the confirmation — the assistant never
// submits on its own.
export interface AssistantAction {
  kind: 'confirm_payment';
  method: string;
  totalCents: number;
  scheduledFor?: string;
}

// One turn of the payer-side agent pipeline (Bill Intelligence → Financial Planning). The reply is
// the assistant's payer-facing message; the recommendation is the deterministic plan it selected —
// method, timing, and the server-authoritative fee/total the payer would confirm.
export interface AssistantRecommendation {
  reply: string;
  method: string;
  scheduledFor?: string;
  feeCents: number;
  totalCents: number;
  rationale: string;
  action?: AssistantAction;
}

// A single turn in the payer/assistant conversation. Sent to the assistant so it can answer the
// latest question in the context of the bill; also what the UI renders as the transcript.
export interface AssistantMessage { role: 'user' | 'assistant'; content: string; }

export interface PaymentExperienceProvider {
  getInvoices(accountNumber: string): Promise<Invoice[]>;
  findPayer(accountNumber: string): Promise<PayerProfile | undefined>;
  updatePreferences(payerId: string, preferences: PayerProfile['preferences']): Promise<PayerProfile['preferences']>;
  getPayments(payerId: string): Promise<PaymentHistory[]>;
  quote(invoiceId: string, method: string, amountCents?: number): Promise<PaymentQuote>;
  askAssistant(invoiceId: string, accountNumber: string): Promise<AssistantRecommendation>;
  chat(invoiceId: string, accountNumber: string, messages: AssistantMessage[]): Promise<AssistantRecommendation>;
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
  // An optional requested amount prices a partial payment; the server validates it against the balance.
  quote(invoiceId: string, method: string, amountCents?: number) { return observed('pwa.payment.quote', async () => {
    const amountParam = amountCents !== undefined ? `&amount_cents=${encodeURIComponent(amountCents)}` : '';
    const payload = await read<{ fee_cents: number; total_cents: number; amount_cents: number; outstanding_cents: number }>(await fetchWithTimeout(`/payments/quote?biller_id=${encodeURIComponent(this.billerId)}&invoice_id=${encodeURIComponent(invoiceId)}&method=${encodeURIComponent(method)}${amountParam}`, { headers: this.headers() }));
    return { feeCents: payload.fee_cents, totalCents: payload.total_cents, amountCents: payload.amount_cents, outstandingCents: payload.outstanding_cents };
  }); }

  // Runs one turn of the payer-side agent pipeline through the Biller Experience API (which reaches
  // the services via the MCP router). The biller is bound server-side; the browser never passes it
  // as an identity argument beyond the correlation header.
  askAssistant(invoiceId: string, accountNumber: string) {
    return observed('pwa.assistant.turn', () => this.turn(invoiceId, accountNumber));
  }

  // A follow-up turn: the payer's message history is sent so the assistant answers the latest
  // question grounded in the same bill. Identity stays server-bound; only the correlation header
  // and the account number (for lookup) leave the browser.
  chat(invoiceId: string, accountNumber: string, messages: AssistantMessage[]) {
    return observed('pwa.assistant.turn', () => this.turn(invoiceId, accountNumber, messages));
  }

  private async turn(invoiceId: string, accountNumber: string, messages?: AssistantMessage[]): Promise<AssistantRecommendation> {
    const payload = await read<{
      reply: string;
      artifacts: {
        payment_plan: { method: string; when: string; fee_cents: number; total_cents: number; rationale: string };
        action?: { kind: string; method: string; total_cents: number; scheduled_for?: string };
      };
    }>(
      await fetchWithTimeout(`/api/billers/${encodeURIComponent(this.billerId)}/payer-chat`, {
        method: 'POST',
        headers: this.headers(true),
        body: JSON.stringify({ invoice_id: invoiceId, account_number: accountNumber, ...(messages?.length ? { messages } : {}) }),
      }));
    const plan = payload.artifacts.payment_plan;
    const raw = payload.artifacts.action;
    const action: AssistantAction | undefined = raw?.kind === 'confirm_payment'
      ? { kind: 'confirm_payment', method: raw.method, totalCents: raw.total_cents, scheduledFor: raw.scheduled_for }
      : undefined;
    return { reply: payload.reply, method: plan.method, scheduledFor: plan.when === 'now' ? undefined : plan.when, feeCents: plan.fee_cents, totalCents: plan.total_cents, rationale: plan.rationale, action };
  }

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
    // An installment plan responds with a schedule (its first installment stands in for the receipt);
    // a one-time payment (full or partial) responds with a single payment. Amount/plan are optional so
    // the default full-payment body is byte-for-byte unchanged.
    const raw = await read<PaymentOrPlanResponse>(await fetchWithTimeout('/payments', {
      method: 'POST',
      headers: { ...this.headers(true), 'idempotency-key': request.idempotencyKey },
      body: JSON.stringify({ biller_id: this.billerId, invoice_id: request.invoiceId, method: request.method, payer_account_id: payer?.payer_id, scheduled_for: request.scheduledFor, amount_cents: request.amountCents, installment_count: request.installmentCount }),
    }));
    const payment = summarizePayment(raw);
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
    return { confirmation: payment.confirmation, amountCents: payment.amount_cents, feeCents: payment.fee_cents, totalCents: payment.total_cents, status: payment.status, scheduledFor: payment.scheduled_for, payerAccountId: payer?.payer_id, preferenceUpdateFailed, installmentPlanId: payment.installment_plan_id, installmentCount: payment.installment_count };
  }); }
}

interface PaymentPayload { confirmation: string; amount_cents: number; fee_cents: number; total_cents: number; status: string; scheduled_for?: string; installment_plan_id?: string; installment_count?: number }
interface InstallmentPlanPayload { installment_plan_id: string; installment_count: number; total_amount_cents: number; installments: PaymentPayload[] }
type PaymentOrPlanResponse = PaymentPayload | InstallmentPlanPayload;

// Collapse either response shape to a single payment summary. For an installment plan the receipt
// reports the whole plan: the plan principal, and the fees/totals rolled up from the server-computed
// per-installment values (no fee/total is computed here — these are sums of amounts the server
// already priced). The plan's status stays 'scheduled' since no installment has been charged yet,
// and the first installment supplies the confirmation and earliest scheduled date for the receipt.
function summarizePayment(raw: PaymentOrPlanResponse): PaymentPayload {
  if ('installments' in raw) {
    const first = raw.installments[0];
    return {
      ...first,
      amount_cents: raw.total_amount_cents,
      fee_cents: raw.installments.reduce((sum, installment) => sum + installment.fee_cents, 0),
      total_cents: raw.installments.reduce((sum, installment) => sum + installment.total_cents, 0),
      installment_plan_id: raw.installment_plan_id,
      installment_count: raw.installment_count,
    };
  }
  return raw;
}

async function read<T>(response: Response): Promise<T> { if (!response.ok) throw await requestError(response, `Request failed with ${response.status}.`); return await response.json() as T; }
