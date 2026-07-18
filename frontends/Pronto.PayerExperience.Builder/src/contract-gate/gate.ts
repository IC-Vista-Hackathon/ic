import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { chromium, type Page } from '@playwright/test';
import type { ContractGateReport, ContractObservations } from './contract';
import { evaluateContract } from './evaluate';
import { buildObservations, drivePaymentFlowByRoles } from './drive';
import { createPaymentRecorder, installHarness, DEFAULT_CONFIRMATION, type InvoiceFixture } from './harness';
import { serveBundle } from '../serve';

export interface ContractGateInputs {
  distDir: string;
  slug: string;
  definitionPath: string;
  // Overridable for tests; defaults to the shared deterministic invoice fixture.
  invoices?: InvoiceFixture[];
  serverConfirmation?: string;
}

// The F6 runtime boundary gate: serve the freshly built bundle, mount it in a real browser
// with the deterministic mock harness, drive it by accessibility roles, capture the payment
// request(s) it emits, and assert they conform to the payment contract. Returns a
// machine-readable report consistent with the F5 static gate.
export async function runContractGate(inputs: ContractGateInputs): Promise<ContractGateReport> {
  const definition = JSON.parse(readFileSync(resolve(inputs.definitionPath), 'utf8'));
  const site = await serveBundle(inputs.distDir, inputs.slug);
  try {
    return await runContractGateAgainstUrl({
      url: site.url,
      definition,
      invoices: inputs.invoices,
      serverConfirmation: inputs.serverConfirmation,
    });
  } finally {
    await site.close();
  }
}

export interface UrlGateInputs {
  url: string;
  definition: unknown;
  invoices?: InvoiceFixture[];
  serverConfirmation?: string;
  // How long the mocked payment response is held so the driver's rapid second confirm press
  // races the first in-flight request (defaults to 250ms).
  paymentLatencyMs?: number;
}

// Same gate against an already-served URL, launching its own browser — the seam the runtime
// gate uses and a convenience for callers without a Playwright page in hand.
export async function runContractGateAgainstUrl(inputs: UrlGateInputs): Promise<ContractGateReport> {
  const browser = await chromium.launch();
  try {
    // Block the service worker so the harness routes intercept every fetch.
    const context = await browser.newContext({ serviceWorkers: 'block' });
    const page = await context.newPage();
    return await runContractGateOnPage(page, inputs);
  } finally {
    await browser.close();
  }
}

// The core gate against a caller-supplied page: install the deterministic harness, drive the
// flow by roles, capture the emitted requests, and evaluate them. The fixture e2e tests drive
// this directly with the Playwright-provided page.
export async function runContractGateOnPage(page: Page, inputs: UrlGateInputs): Promise<ContractGateReport> {
  const serverConfirmation = inputs.serverConfirmation ?? DEFAULT_CONFIRMATION;
  const recorder = createPaymentRecorder();
  await installHarness(page, {
    definition: inputs.definition,
    invoices: inputs.invoices,
    paymentResponse: { confirmation: serverConfirmation, amount_cents: 12500, fee_cents: 250, total_cents: 12750, status: 'succeeded' },
    recorder,
    // Hold the payment response briefly so the driver's rapid second confirm press races the
    // first request while it's still in flight — this is what actually exercises the bundle's
    // exactly-once guard rather than letting the two presses serialize.
    delayMs: inputs.paymentLatencyMs ?? 250,
  });

  const drive = await drivePaymentFlowByRoles(page, inputs.url, serverConfirmation);
  const knownInvoiceIds = (inputs.invoices ?? [{ id: 'inv-1' } as InvoiceFixture]).map(invoice => invoice.id);
  const observations = buildObservations({ recorder, knownInvoiceIds, serverConfirmation, drive });
  return report(observations, serverConfirmation);
}

function report(observations: ContractObservations, serverConfirmation: string): ContractGateReport {
  const violations = evaluateContract(observations);
  return {
    passed: violations.length === 0,
    generatedAt: new Date().toISOString(),
    drivenBy: 'accessibility-roles',
    paymentRequestCount: observations.paymentPosts.length,
    serverConfirmation,
    violations,
  };
}
