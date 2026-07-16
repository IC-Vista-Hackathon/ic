# Pronto.Functional.Tests

Black-box functional tests that drive a **deployed** Pronto environment over HTTP (nonprod by
default). They enforce the acceptance criteria in
[`docs/pronto-functional-requirements.md`](../../docs/pronto-functional-requirements.md); the rules
of the road are in [`docs/pronto-functional-testing-policy.md`](../../docs/pronto-functional-testing-policy.md).

## Key facts

- **In `Pronto.slnx`** (per repo convention) so the solution-wide build/test keeps it compiling,
  but **inert in per-PR CI**: every test skips when `PRONTO_FUNCTIONAL_BASE_URL` is unset, and CI
  never sets it. The suite runs for real only in the nonprod deploy workflow.
- **Target env via `PRONTO_FUNCTIONAL_BASE_URL`** — the gateway origin, e.g.
  `http://pronto-nonprod.eastus2.cloudapp.azure.com`. When unset, **every test skips**.
- **Nonprod only.** Never point the base URL at the prod gateway.
- Test billers use unique random slugs and are purged after the run via the nonprod-only
  `DELETE /api/internal/test-data` (biller-experience) and `DELETE /invoices/internal/test-data`
  (seeded invoices) maintenance endpoints.

## Categories

- `functional` — all tests here.
- `known-gap` — behaviors Pronto should have but does **not** yet; written to fail today, pass once
  fixed. When you fix the feature, delete the `known-gap` trait so the test joins the blocking gate.

## Run

```bash
# Full suite against nonprod
PRONTO_FUNCTIONAL_BASE_URL=http://pronto-nonprod.eastus2.cloudapp.azure.com \
  dotnet test tests/Pronto.Functional.Tests

# Blocking gate only (must be green to promote nonprod -> prod)
PRONTO_FUNCTIONAL_BASE_URL=http://pronto-nonprod.eastus2.cloudapp.azure.com \
  dotnet test tests/Pronto.Functional.Tests --filter "Category=functional&Category!=known-gap"

# Known gaps only (expected-fail today)
PRONTO_FUNCTIONAL_BASE_URL=http://pronto-nonprod.eastus2.cloudapp.azure.com \
  dotnet test tests/Pronto.Functional.Tests --filter "Category=known-gap"
```

## What's covered today

| Test | Requirement | State |
| --- | --- | --- |
| `HealthAndConfigTests` | FR-9 | passing |
| `OnboardingBootstrapTests` | FR-1, FR-2 | passing |
| `AgenticInvoiceSeedingTests` | FR-6 | **known-gap** (hard-coded invoice templates) |
| `BrandResearchTests` | FR-3, FR-4 | **known-gap** (research doesn't scrape brand) |
| `PrematureBrandingTests` | FR-5 | **known-gap** (fabricated branding before research) |
