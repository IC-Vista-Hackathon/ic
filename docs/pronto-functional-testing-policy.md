# Pronto — Functional Testing Policy

Status: **living document**. This defines how we keep confidence in Pronto as we iterate: what kinds
of tests we write, where they live, how they gate merges, and how they evolve with the product.

The guiding rule: **a feature is not "done" until a requirement in
`docs/pronto-functional-requirements.md` describes it and a test enforces it against a real,
deployed environment.**

## The test pyramid for this repo

| Layer | Where | Runs against | When it runs |
| --- | --- | --- | --- |
| **Unit** | `tests/Pronto.*.Tests` | in-process, no I/O | every PR (CI `dotnet test`) |
| **Integration** | `tests/Pronto.BillerExperience.IntegrationTests` | in-process host (`WebApplicationFactory`) | every PR (CI `dotnet test`) |
| **Functional (deployed)** | `tests/Pronto.Functional.Tests` | **deployed nonprod** over HTTP | nonprod deploy workflow, before promote-to-prod |
| **Browser / UI** | `tests/browser-smoke` (Playwright) | deployed frontends | nonprod deploy workflow |

Deterministic logic (discovery rules, invoice generation, state transitions, money movement) is
covered by unit/integration tests. **Agentic and cross-service behavior that only shows up in a real
deployment** — research, brand extraction, seeding relevance, preview ordering — is covered by the
functional layer, because that is the only place it is real.

## The deployed functional suite (`tests/Pronto.Functional.Tests`)

- **Black-box over HTTP.** It drives the same gateway routes the Studio and payer PWA use
  (`/api`, `/invoices`), reading responses as raw JSON so tests assert the wire contract, not
  server internals.
- **Registered in `Pronto.slnx`** (per the repo convention) so the solution-wide build/test keeps
  it compiling, yet it stays inert in per-PR CI: every test skips when `PRONTO_FUNCTIONAL_BASE_URL`
  is unset (CI never sets it), so `dotnet test Pronto.slnx` builds it and skips it rather than
  requiring a reachable environment. The suite runs for real only in the nonprod deploy workflow.
- **Target via `PRONTO_FUNCTIONAL_BASE_URL`** (the gateway origin, e.g.
  `http://pronto-nonprod.eastus2.cloudapp.azure.com`). When unset, **every test skips** rather than
  fails — the project is inert locally until you point it at an environment.
- **Nonprod only.** Never set the base URL to the prod gateway.

### Categories and how they gate merges

Tests carry xUnit `Category` traits (`tests/Pronto.Functional.Tests/Categories.cs`):

- `functional` — every test in the suite.
- `known-gap` — additionally marks a behavior we *want* but do **not** yet have. These are written to
  **fail today** and pass once the feature is fixed.

Two runs, two meanings:

```bash
# Blocking gate — must be green before nonprod promotes to prod:
dotnet test tests/Pronto.Functional.Tests --filter "Category=functional&Category!=known-gap"

# Known gaps — expected-fail today; reported, non-blocking, until the feature lands:
dotnet test tests/Pronto.Functional.Tests --filter "Category=known-gap"
```

**Why known-gap tests are non-blocking (for now):** the user asked for tests that fail today to pin
the three known defects. Making them a hard gate immediately would block every unrelated PR. Instead
they run in a visible, `continue-on-error` step so the failures are on the record without wedging the
repo. **As each feature is fixed, delete the `known-gap` trait** so the test moves into the blocking
gate and stays there — this is the ratchet that grows our confidence over time.

### Environment & data isolation

- Every test biller uses a unique random slug (`fn-<guid>`).
- The suite purges what it creates via the nonprod-only maintenance endpoints (enabled by
  `Maintenance:PurgeEnabled`, 404 in prod). Because each service owns its own store with no
  cross-service cascade, cleanup hits each service directly: `DELETE /api/internal/test-data`
  (biller-experience: configs/runs/deployments/biller) and `DELETE /invoices/internal/test-data`
  (the seeded demo invoices). The suite does not create PayerAccount records, so none are purged.
- Cleanup is best-effort in `Dispose` and never fails a run; nonprod tolerates orphaned demo data.

### Flake, timeouts, retries

- No fixed `sleep`s for "let it settle" — poll with a bounded loop and a clear terminal assertion.
- Network calls use a bounded `HttpClient` timeout; failures throw `ProntoApiException` carrying the
  request URI, status code, and body.
- A genuinely flaky functional test is a bug in the test or a real intermittent product defect —
  quarantine by moving it to `known-gap` with a linked issue, don't add blind retries.

### Diagnostics

- Assertion messages state the expected vs. actual product behavior in plain language
  (e.g. "Two unrelated billers received identical seeded invoices; seeding is a fixed template").
- Research assertions read the activity feed and report agent id / status / error code.

## Naming & acceptance criteria

- One requirement (`FR-n`) per behavior in `docs/pronto-functional-requirements.md`, with Gherkin
  acceptance criteria. Change the requirement **before** the test.
- Test class names describe the behavior area; method names are the concrete scenario
  (`SeededInvoicesAreRelevantToBillerNotFixedTemplate`).
- Each test's XML doc cites its `FR-n` and, if `known-gap`, the exact code path responsible.

## CI / workflow placement

- **Unit + integration:** `.github/workflows/ci.yml` (`dotnet test` on the solution) — every PR.
- **Deployed functional + browser:** `.github/workflows/deploy-nonprod.yml`, after the PR head is
  deployed to `ic-nonprod` and rollout/health checks pass. The blocking functional gate runs first;
  the known-gap step runs `continue-on-error`. This is the pre-merge confidence gate: nonprod green ⇒
  safe to merge ⇒ deploy to prod.

## Promotion / merge gate

1. PR opened → unit + integration tests (CI).
2. Maintainer labels `safe-to-deploy` → deploy PR head to nonprod.
3. Rollout + health + smoke checks pass.
4. **Blocking functional gate** (`functional & !known-gap`) passes.
5. Known-gap suite runs and reports (non-blocking) — a shrinking list of documented defects.
6. Merge → deploy to prod.

## Evolving the suite

- **New feature:** add `FR-n` + Gherkin, then a `functional` test; it must be green before merge.
- **Fixing a known gap:** make it pass, then remove the `known-gap` trait so it joins the blocking
  gate. Update the corresponding `FR-n` (drop the "Known gap" note).
- **Changed behavior:** update the requirement first, then the test — never edit a test purely to make
  it pass.
- **New defect found in nonprod:** capture it as a `known-gap` test + `FR-n` note so it's tracked and
  can't silently regress after it's fixed.

## Quick start

```bash
# Run the full suite against nonprod locally:
PRONTO_FUNCTIONAL_BASE_URL=http://pronto-nonprod.eastus2.cloudapp.azure.com \
  dotnet test tests/Pronto.Functional.Tests

# Just the blocking gate:
PRONTO_FUNCTIONAL_BASE_URL=http://pronto-nonprod.eastus2.cloudapp.azure.com \
  dotnet test tests/Pronto.Functional.Tests --filter "Category=functional&Category!=known-gap"
```
