import type { Invoice, PaymentReceipt, PaymentRequest } from './types';
import { observed } from './telemetry';

export interface PaymentExperienceProvider { getInvoice(accountNumber: string): Promise<Invoice>; pay(request: PaymentRequest): Promise<PaymentReceipt> }

export class DemoPaymentExperienceProvider implements PaymentExperienceProvider {
  getInvoice(accountNumber: string) { return observed('pwa.invoice.lookup', async () => ({ id: 'invoice-demo-1', accountNumber, amountCents: 12842, dueDate: '2026-08-04', description: 'Utility services' })); }
  pay(request: PaymentRequest) { return observed('pwa.payment.demo', async () => { await new Promise(resolve => setTimeout(resolve, 650)); return { confirmation: `DEMO-${crypto.randomUUID().slice(0, 8).toUpperCase()}`, amountCents: 12842, feeCents: request.method === 'card' ? 250 : 95 }; }); }
}
