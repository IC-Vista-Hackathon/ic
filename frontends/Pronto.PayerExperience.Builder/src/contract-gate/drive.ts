import type { Page } from '@playwright/test';
import type { ContractObservations } from './contract';
import type { PaymentRecorder } from './harness';
import { DEFAULT_ACCOUNT_NUMBER } from './harness';

// Drives the payer flow purely by ACCESSIBILITY ROLES / accessible names — never fixed
// data-testids — so it survives a generated authorable structure (F3 cart, F4 installments)
// that has no stable testids. Anything that goes wrong is surfaced as a flowError rather than
// throwing, so the gate can report it as a machine-readable violation.
export interface DriveOptions {
  accountNumber?: string;
  timeoutMs?: number;
}

export interface DriveResult {
  renderedConfirmation: string | null;
  flowError?: string;
}

export async function drivePaymentFlowByRoles(page: Page, url: string, serverConfirmation: string, options: DriveOptions = {}): Promise<DriveResult> {
  const timeout = options.timeoutMs ?? 15_000;
  const account = options.accountNumber ?? DEFAULT_ACCOUNT_NUMBER;
  try {
    await page.goto(url);

    // Scope to the main landmark so we never pick up chrome/nav controls (e.g. a "Pay Bill"
    // nav button) — only the payment flow's own accessible controls.
    const main = page.getByRole('main');

    const accountField = main.getByRole('textbox', { name: /account/i }).first();
    await accountField.waitFor({ state: 'visible', timeout });
    await accountField.fill(account);

    await main.getByRole('button', { name: /continue|find|look ?up/i }).first().click();

    // Method selection — pick the card method by its accessible label.
    const cardMethod = main.getByRole('button', { name: /^card/i }).first();
    await cardMethod.waitFor({ state: 'visible', timeout });
    await cardMethod.click();

    // Advance to review.
    await main.getByRole('button', { name: /review/i }).first().click();

    // Confirm the payment. Exclude "Review Payment" by anchoring on the start of the label.
    const payButton = main.getByRole('button', { name: /^(pay|processing|confirm|schedule)/i }).first();
    await payButton.waitFor({ state: 'visible', timeout });
    // Fire the confirm control twice in the same synchronous tick — a real rapid double-press —
    // to actually exercise the bundle's exactly-once / in-flight guard. Paired with the harness's
    // payment latency (delayMs), the second press races the first still-in-flight request: a
    // compliant bundle collapses this to a single POST, while one lacking double-submit
    // protection emits two and fails the gate. Dispatching in-page (rather than two awaited
    // Playwright clicks) is what makes the two presses race instead of serialize.
    await payButton.evaluate((el: { click: () => void }) => {
      el.click();
      el.click();
    });

    const rendered = await readRenderedConfirmation(page, serverConfirmation, timeout);
    return { renderedConfirmation: rendered };
  } catch (error) {
    const rendered = await readRenderedConfirmation(page, serverConfirmation, 1_000).catch(() => null);
    return { renderedConfirmation: rendered, flowError: error instanceof Error ? error.message : String(error) };
  }
}

// Reads the confirmation the UI rendered without relying on a testid: it waits for the exact
// server confirmation to become visible (compliant flows), and otherwise extracts whatever
// confirmation-like token the page is showing so a mismatch is reported with the wrong value.
async function readRenderedConfirmation(page: Page, serverConfirmation: string, timeout: number): Promise<string | null> {
  try {
    await page.getByText(serverConfirmation, { exact: false }).first().waitFor({ state: 'visible', timeout });
    return serverConfirmation;
  } catch {
    const bodyText = await page.locator('body').innerText().catch(() => '');
    return extractConfirmationToken(bodyText);
  }
}

// Best-effort: pull a confirmation-looking token (e.g. PRONTO-XXXX) out of the visible text for
// diagnostics when the true server confirmation isn't present.
function extractConfirmationToken(text: string): string | null {
  const labelled = text.match(/confirmation[^A-Za-z0-9]*([A-Za-z0-9][A-Za-z0-9-]{3,})/i);
  if (labelled) return labelled[1];
  const token = text.match(/\b[A-Z]{3,}-[A-Z0-9]{4,}\b/);
  return token ? token[0] : null;
}

export function buildObservations(input: {
  recorder: PaymentRecorder;
  knownInvoiceIds: string[];
  serverConfirmation: string;
  drive: DriveResult;
}): ContractObservations {
  return {
    knownInvoiceIds: input.knownInvoiceIds,
    serverConfirmation: input.serverConfirmation,
    renderedConfirmation: input.drive.renderedConfirmation,
    paymentPosts: input.recorder.posts,
    flowError: input.drive.flowError,
  };
}
