import { describe, expect, it, vi } from 'vitest';
import { settleInvoices } from './batch';
import type { PaymentReceipt, PaymentRequest } from './types';

function request(invoiceId: string, idempotencyKey: string): PaymentRequest {
  return { invoiceId, method: 'card', autoPay: false, paperless: false, accountNumber: '4421', idempotencyKey };
}
function receipt(invoiceId: string): PaymentReceipt {
  return { confirmation: `CONF-${invoiceId}`, amountCents: 1, feeCents: 0, totalCents: 1, status: 'succeeded' };
}

describe('settleInvoices', () => {
  it('loops the single-invoice pay once per request, preserving each distinct key', async () => {
    const seen: PaymentRequest[] = [];
    const pay = vi.fn(async (req: PaymentRequest) => { seen.push(req); return receipt(req.invoiceId); });
    const requests = [request('inv-1', 'key-1'), request('inv-2', 'key-2'), request('inv-3', 'key-3')];

    const result = await settleInvoices(pay, requests);

    expect(pay).toHaveBeenCalledTimes(3);
    expect(seen.map(req => req.idempotencyKey)).toEqual(['key-1', 'key-2', 'key-3']);
    expect(result.allSucceeded).toBe(true);
    expect(result.outcomes.every(outcome => outcome.receipt)).toBe(true);
  });

  it('captures a per-invoice failure without aborting the batch or losing successes', async () => {
    const pay = vi.fn(async (req: PaymentRequest) => {
      if (req.invoiceId === 'inv-2') throw new Error('processor unavailable');
      return receipt(req.invoiceId);
    });
    const requests = [request('inv-1', 'key-1'), request('inv-2', 'key-2'), request('inv-3', 'key-3')];

    const result = await settleInvoices(pay, requests);

    expect(pay).toHaveBeenCalledTimes(3);
    expect(result.allSucceeded).toBe(false);
    expect(result.outcomes.find(outcome => outcome.invoiceId === 'inv-1')?.receipt).toBeDefined();
    expect(result.outcomes.find(outcome => outcome.invoiceId === 'inv-3')?.receipt).toBeDefined();
    const failed = result.outcomes.find(outcome => outcome.invoiceId === 'inv-2');
    expect(failed?.receipt).toBeUndefined();
    expect(failed?.error).toContain('processor unavailable');
  });
});
