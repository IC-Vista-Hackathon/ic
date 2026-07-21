// Live real-agent UI journey smoke for a DEPLOYED Pronto environment (usually ic-nonprod through a
// port-forwarded gateway; also runnable against any reachable gateway origin).
//
// Unlike the deterministic Playwright UI E2E in the frontends (which fakes the backend at the
// network boundary), this drives the *real* deployed stack with NOTHING stubbed: it exercises the
// live Biller Experience agents (Discovery/Analysis) and the deterministic Invoice/Payment/
// PayerAccount services through the shipped Studio + payer PWA. It is the automated version of
// "go through the whole creation process by hand".
//
// The journey, entirely through real UI (real clicks, real forms, real iframe, real fetches):
//   Studio: /studio/ → Try It Out → pick vertical → business details → setup path → Build My
//           Preview → (REAL agents analyze) → Review agent findings → Preview My Payment Site →
//           the preview embeds the shipped payer PWA against the freshly-provisioned, seeded
//           preview tenant.
//   Payer:  inside that preview iframe → enter the seeded account → find bill → choose card →
//           review → pay (fake rails) → confirmation.
//
// Failure policy, mirroring telemetry-smoke.mjs's treatment of AI ingestion throttling (439):
//   * Frontend/UI regressions the smoke exists to catch — the app white-screens, a wizard step's
//     control is missing, or (the original bug) the preview iframe renders blank — HARD FAIL.
//   * The real agent analysis not completing within the budget, or the orchestration surfacing an
//     error banner, is an agent availability/latency/quota condition (infra), NOT a frontend
//     regression: it is reported as a ::warning:: and exits 0. The deployed .NET functional tests
//     own hard backend correctness; keeping agent latency non-blocking here stops AI-quota blips
//     from failing the shared-environment deploy.
//
// Prints a single JSON object on stdout ({ stage, agentsExercised, confirmation, warned }); all
// logging goes to stderr so the caller can pipe stdout into jq.
//
// Usage: TARGET_ORIGIN=http://127.0.0.1:18081 node live-journey-smoke.mjs

import { chromium } from 'playwright';

const origin = process.env.TARGET_ORIGIN ?? 'http://127.0.0.1:18081';
// The seeded demo account every provisioned preview tenant attaches its invoices + payer to
// (SeedDefaults.PreviewAccountNumber). The preview PWA defaults to it; we fill it explicitly.
const account = process.env.LIVE_SMOKE_ACCOUNT ?? '4421';
// Real Discovery/Analysis agents run server-side on Build My Preview; give them a generous budget
// (the Studio client itself waits on the 300s compliance/chat budget). Overridable for slow envs.
const analysisTimeoutMs = Number(process.env.ANALYSIS_TIMEOUT_MS ?? 240_000);
const stepTimeoutMs = Number(process.env.STEP_TIMEOUT_MS ?? 30_000);
const businessName = process.env.LIVE_SMOKE_BUSINESS ?? 'Pronto Live Smoke Water';

const log = message => console.error(`[live-journey-smoke] ${message}`);
const fail = message => { log(`FAIL: ${message}`); process.exit(1); };
// A non-fatal infra/agent-availability condition: annotate for the workflow log, emit the result,
// and exit 0. `::warning::` must start the line for GitHub Actions to render it as an annotation.
function warnAndExit(stage, message) {
  console.error(`::warning::Live journey smoke stopped at "${stage}": ${message}. This is an agent availability/latency condition (the real Discovery/Analysis pipeline), not a frontend regression — the deployed functional tests gate backend correctness.`);
  console.log(JSON.stringify({ stage, agentsExercised: false, confirmation: null, warned: true }));
  process.exit(0);
}

const browser = await chromium.launch();
const context = await browser.newContext();
const page = await context.newPage();
page.setDefaultTimeout(stepTimeoutMs);
page.on('console', message => { if (message.type() === 'error') log(`console.error: ${message.text().slice(0, 200)}`); });
page.on('pageerror', error => log(`pageerror: ${String(error).slice(0, 200)}`));

// page-level response events also fire for the preview subframe, so this captures both the Studio
// shell and the embedded payer PWA. A 5xx anywhere in the live stack is a backend availability
// condition (infra), while a 404 on an HTML document is the routing/blank-shell regression this
// smoke exists to catch — the two are classified differently when a UI step fails to render.
const serverErrors = [];
const documentNotFound = [];
page.on('response', response => {
  const status = response.status();
  if (status >= 500) serverErrors.push(`${status} ${response.request().method()} ${response.url()}`);
  else if (status === 404 && response.request().resourceType() === 'document') documentNotFound.push(response.url());
});
// A UI step failed to render. If the deployed backend returned a 5xx during this run it is an infra
// condition (warn, non-fatal, mirroring telemetry-smoke's 439 handling); otherwise it is a genuine
// frontend/UI regression (hard fail).
const failOrWarn = (stage, message) =>
  serverErrors.length
    ? warnAndExit(stage, `${message}; the deployed backend returned ${serverErrors.length} 5xx response(s) this run (latest: ${serverErrors.slice(-2).join(' | ')})`)
    : fail(message);

try {
  // ---- Phase A: Studio boots and the onboarding wizard renders (no agents; hard-fail regressions).
  log(`loading ${origin}/studio/`);
  await page.goto(`${origin}/studio/`, { waitUntil: 'domcontentloaded', timeout: stepTimeoutMs });
  await page.getByRole('button', { name: 'Try It Out' }).first().click()
    .catch(() => fail('the Studio landing did not render a "Try It Out" entry point'));

  await page.getByRole('heading', { name: 'What line of business is this for?' }).waitFor()
    .catch(() => fail('the vertical-selection wizard step did not render'));
  await page.getByRole('button', { name: /Utilities/ }).click();
  await page.getByRole('button', { name: 'Continue' }).click();

  await page.getByRole('heading', { name: 'Business Details' }).waitFor()
    .catch(() => fail('the business-details wizard step did not render'));
  await page.getByPlaceholder('e.g. Your Organization').fill(businessName);
  await page.getByPlaceholder('Search states...').fill('California');
  await page.getByRole('button', { name: 'California' }).click();
  // The Upload path needs no manual billing-category entry, keeping the walk deterministic while
  // still satisfying each step's validation.
  await page.getByRole('button', { name: /Upload biller data/ }).click();
  await page.getByRole('button', { name: 'Continue' }).click();

  await page.getByRole('heading', { name: 'Brand Details' }).waitFor()
    .catch(() => fail('the brand-details wizard step did not render'));
  log('wizard complete; launching Build My Preview (real agents run now)');
  await page.getByRole('button', { name: 'Build My Preview' }).click();

  // ---- Phase B: the REAL Discovery/Analysis agents run. Completion => the review CTA appears.
  // Not completing in budget, or an orchestration error banner, is treated as an agent-infra
  // warning rather than a frontend regression (see failure policy above).
  const reviewCta = page.getByRole('button', { name: 'Review agent findings' });
  // The app renders this exact alert only when the orchestration itself errors out (App.tsx
  // `orchestrationError`); match it precisely so the benign "Research orchestration" heading on the
  // analyzing screen is never mistaken for a failure.
  const orchestrationError = page.getByText('We could not finish building this preview.');
  log(`waiting up to ${analysisTimeoutMs}ms for the real agent analysis to complete`);
  const analysisOutcome = await Promise.race([
    reviewCta.waitFor({ timeout: analysisTimeoutMs }).then(() => 'ready').catch(() => 'timeout'),
    orchestrationError.waitFor({ timeout: analysisTimeoutMs }).then(() => 'error').catch(() => 'timeout'),
  ]);
  if (analysisOutcome === 'error') warnAndExit('agent-analysis', 'the Studio surfaced an orchestration/agent error');
  if (analysisOutcome !== 'ready') warnAndExit('agent-analysis', `no "Review agent findings" within ${analysisTimeoutMs}ms`);
  log('real agent analysis completed');

  // ---- Phase C: review → preview. The preview embeds the shipped payer PWA against the seeded
  // preview tenant. The original bug was a BLANK iframe, so a non-rendering payer app fails (unless
  // a backend 5xx explains it — see failOrWarn). The payer copy is agent/vertical-authored (e.g.
  // "Pay your utilities bill"), so the vertical-agnostic find-bill input is the render signal.
  await reviewCta.click();
  await page.getByRole('heading', { name: "Let's Review Things" }).waitFor()
    .catch(() => fail('the agent-findings review screen did not render'));
  await page.getByRole('button', { name: /Preview My Payment Site/ }).click();
  await page.getByTestId('preview-badge').waitFor()
    .catch(() => fail('the preview screen did not open'));

  const preview = page.frameLocator('[data-testid="preview-frame"]');
  const rendered = await preview.getByTestId('account-input').waitFor({ timeout: 25_000 }).then(() => true).catch(() => false);
  if (!rendered) {
    if (documentNotFound.length) fail(`the preview iframe shell 404'd — the blank-preview routing regression (${documentNotFound.join(', ')})`);
    failOrWarn('preview', 'the preview iframe did not render the payer PWA (blank-preview regression)');
  }
  log('preview iframe renders the real payer PWA against the seeded tenant');

  // ---- Phase D: drive the real payer pay flow inside the preview iframe (real seeded services,
  // fake payment rails). This is the deployed Invoice → quote → payment path end to end.
  await preview.getByTestId('account-input').fill(account);
  await preview.getByTestId('lookup-submit').click();
  const billLoaded = await preview.getByTestId('method-card').waitFor({ timeout: 25_000 }).then(() => true).catch(() => false);
  if (!billLoaded) failOrWarn('find-bill', `the seeded bill for account ${account} did not load in the preview`);
  await preview.getByTestId('method-card').click();
  await preview.getByTestId('review-submit').click();
  await preview.getByTestId('pay-submit').click();
  const paid = await preview.getByTestId('payment-confirmation').waitFor({ timeout: 25_000 }).then(() => true).catch(() => false);
  if (!paid) failOrWarn('pay', 'the fake payment did not reach a confirmation receipt');
  // A single-invoice receipt exposes the confirmation code; a multi-invoice (batch) receipt shows a
  // "Payments received" heading with no per-payment code. Prefer the code, fall back to the receipt
  // heading so the result is always meaningful (the assertion above already proved payment succeeded).
  const confirmation =
    (await preview.getByTestId('confirmation-code').first().textContent({ timeout: 4_000 }).catch(() => null))
    ?? (await preview.getByTestId('payment-confirmation').first().textContent({ timeout: 2_000 }).catch(() => null));
  log(`payment confirmed (${confirmation?.trim() || 'receipt shown'})`);

  console.log(JSON.stringify({ stage: 'complete', agentsExercised: true, confirmation, warned: false }));
} finally {
  await context.close();
  await browser.close();
}
