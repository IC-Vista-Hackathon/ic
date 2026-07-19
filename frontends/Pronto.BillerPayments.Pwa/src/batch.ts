import { errorMessage } from './http';
import type { PaymentReceipt, PaymentRequest } from './types';

export interface BatchOutcome {
  invoiceId: string;
  receipt?: PaymentReceipt;
  error?: string;
}

export interface BatchResult {
  outcomes: BatchOutcome[];
  allSucceeded: boolean;
}

// Settle each selected invoice by looping the existing single-invoice, exactly-once
// POST /payments — one call per invoice, each carrying its OWN Idempotency-Key (supplied
// on the request). A per-invoice failure is captured, not thrown, so one decline never
// undoes an already-succeeded charge and the payer sees exactly which invoices settled.
// Re-running with the SAME requests (same keys) is safe: each POST is idempotent
// server-side, so retrying a partially-failed batch never double-charges.
export async function settleInvoices(
  pay: (request: PaymentRequest) => Promise<PaymentReceipt>,
  requests: PaymentRequest[],
): Promise<BatchResult> {
  const outcomes: BatchOutcome[] = [];
  for (const request of requests) {
    try {
      const receipt = await pay(request);
      outcomes.push({ invoiceId: request.invoiceId, receipt });
    } catch (caught) {
      outcomes.push({ invoiceId: request.invoiceId, error: errorMessage(caught) });
    }
  }
  return { outcomes, allSucceeded: outcomes.every(outcome => outcome.receipt !== undefined) };
}
