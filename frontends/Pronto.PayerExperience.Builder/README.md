# Pronto.PayerExperience.Builder

Generates a **bespoke per-biller payer PWA** and publishes the built bundle to Blob
Storage. This is the pipeline behind "the AI writes bespoke React per biller": it turns a
published `BillerExperienceDefinition` into a unique, validated, static SPA served at
`/pay/{slug}`.

## Pipeline

```
assemble design brief
  -> generate skin        (Opus on Azure AI Foundry, or offline deterministic)
  -> persist artifacts     (brief + skin, per biller/revision)
  -> containment gate      (AST allowlist + fixed-core hash manifest — hard fail)   <-- F5
  -> build bundle          (tsc typecheck gate + vite build, base=/pay/{slug}/)
  -> inject CSP            (strict Content-Security-Policy into dist/index.html)     <-- F5
  -> validate bundle       (Playwright UX/a11y smoke, mocked backend)
  -> contract gate         (runtime boundary/payment-contract conformance, mocked backend) <-- F6
  -> publish bundle        (upload full dist/ to Blob Storage, flip active pointer last)
```

The AI only ever authors the **skin** (`src/skin/theme.css` + `src/skin/chrome.tsx` in the
PWA) — the payment flow, service calls, and money logic are stable core. A generated bundle
that fails typecheck, build, the UX smoke, or the contract gate is rejected before publish.

### Contract gate (`src/contract-gate/`)

The correctness precondition for publish. Where the Playwright run is now a pure **UX/a11y
smoke** (flow reachable and operable by accessibility roles) and the F5 static gate proves the
generated *source* stays inside the authorable allowlist, the contract gate proves the built
*bundle* correctly **invokes** the sanctioned payment contract at runtime. It mounts the built
bundle, mocks the backend with the shared harness (`src/contract-gate/harness.ts`), drives the
flow by **accessibility roles/labels** (never `data-testid`s, so it survives a widened,
generated structure), intercepts the network calls, and asserts the emitted requests conform:

- the payment POST carries a well-formed `Idempotency-Key`;
- it references a real invoice id returned by the lookup (no fabricated ids);
- it sets **no** client-controlled amount or fee/total (money comes from server quote/response);
- the confirmation the UI renders equals the confirmation in the (mocked) server response — a
  fabricated/hallucinated confirmation fails the gate;
- one user confirmation → exactly one payment POST (double-submit safe).

The rule engine (`evaluate.ts`) is a pure function, unit-tested exhaustively without a browser
(`evaluate.test.ts`); the browser-driven fixture e2e (`e2e/contract-gate.spec.ts`) proves the
full role-driven capture pipeline — a compliant flow passes and each misbehaving flow fails
with a specific violation. The gate emits a machine-readable report
(`.artifacts/{slug}/{rev}/contract-gate-report.json`) consistent with the F5 gate report.

## Static containment gate (`src/gate/`)

The containment gate makes it *impossible by construction* for agent-generated payer code to
move money or reach arbitrary endpoints. It runs in `pipeline.ts` **before build/publish**
and **hard-fails** (rejecting the bundle) on any violation. It is the safety precondition
for widening the authorable surface (cart, installments) and runs before the boundary/
contract gate and before publish.

The gate has three layers, all reading from a single policy source (`src/gate/contract.ts`):

1. **AST allowlist** — the generated files are statically analyzed:
   - `chrome.tsx` is parsed with the TypeScript compiler API (not regex). Rejected:
     `fetch`/`XMLHttpRequest`/`WebSocket`/`EventSource`/`navigator.sendBeacon`/dynamic
     `import()` (any network access); imports other than the sanctioned contract module
     (`./contract`) — new npm deps and relative imports into the core are rejected
     separately; `dangerouslySetInnerHTML`, `<script>`, inline string event handlers,
     `eval`/`new Function`.
   - `theme.css` is checked lexically (comment-stripped) for external resource origins
     (`url(...)`/`@import` not to a declared font origin), `javascript:` URLs,
     `expression()`, and markup breakout.
   - The authorable allowlist (`AUTHORABLE_FILES`) is the single source of truth and is
     asserted equal to the PWA's `SKIN_EDITABLE_FILES` by a test, so the two never drift as
     the surface widens.
2. **Integrity / provenance** — a committed SHA-256 manifest (`src/gate/core-manifest.json`)
   of the **fixed core** files (App.tsx, provider.ts, http.ts, types.ts, the skin contract,
   the entrypoint/build config). The gate recomputes hashes from the pristine PWA and fails
   if any fixed file was mutated, removed, or added — so generation can never alter the core,
   and a tampered manifest is caught the same way. Regenerate after a *legitimate* core
   change with `npm run gate:manifest`.
3. **Runtime CSP** — a strict `Content-Security-Policy` `<meta>` is injected into the built
   `dist/index.html`: `default-src`/`script-src`/`connect-src 'self'`, `object-src`/
   `frame-ancestors 'none'`. Same-origin API calls (`/invoices`, `/payments`, `/payers`,
   `/api`) are covered by `'self'`; any cross-origin surface must be declared explicitly.

Every run writes a machine-readable report to `<artifacts>/<slug>/<revision>/gate-report.json`
(`{ passed, violations[], coreFilesVerified, ... }`) that the pipeline logs and a later
publish step can attach as evidence.

### Declaring extra origins

Font CDNs and any telemetry ingestion host are **not** same-origin, so they must be declared
or the CSP (and the CSS origin check) will block them. Pass them through `PipelineOptions.containment`:

```ts
containment: {
  fontOrigins: ['https://fonts.gstatic.com'],       // font-src + CSS url()/@import allowlist
  cspConnectOrigins: ['https://<region>.in.applicationinsights.azure.com'], // App Insights
}
```

> Note: the default CSP is `connect-src 'self'`, which will block the PWA's Application
> Insights browser telemetry (cross-origin ingestion). To keep telemetry working in prod,
> the ingestion origin must be passed via `cspConnectOrigins` (coordinate with the browser
> observability feature). The Builder's Playwright gate mocks all backends same-origin, so it
> is unaffected.

## Usage

```bash
npm ci
npx playwright install chromium        # once, for the validation gate

# Generate + build + validate a biller (offline, no external calls):
npm run build:one -- \
  --definition ../Pronto.BillerPayments.Pwa/public/config.json \
  --slug demo --mode deterministic

# Real bespoke authoring with Claude Opus on Azure AI Foundry:
FOUNDRY_ENDPOINT=https://<foundry>.services.ai.azure.com \
FOUNDRY_OPUS_DEPLOYMENT=claude-opus-4-1 \
npm run build:one -- --definition ./demo.json --slug acme --mode opus

# Also publish to Blob Storage (workload identity / az login):
npm run build:one -- --definition ./demo.json --slug acme --mode opus \
  --publish --storage https://<account>.blob.core.windows.net
```

### Flags

| Flag | Default | Notes |
|---|---|---|
| `--definition` | (required) | Path to the experience definition JSON |
| `--slug` | `biller_id` | Route slug; bundle base is `/pay/{slug}/` |
| `--revision` | `rev-<ts>` | Revision id for storage + artifacts |
| `--mode` | `deterministic` | `deterministic` (offline) or `opus` (Foundry) |
| `--no-validate` | off | Skip the Playwright gate (build gate still runs) |
| `--publish` | off | Upload `dist/` and flip the active pointer |
| `--storage` | `PAYER_STORAGE_ENDPOINT` | Blob endpoint (required with `--publish`) |

## In-cluster: the build Job

In prod the builder runs as a short-lived Kubernetes Job, one per publication. The Deployment
Worker (`Pronto.BillerExperience.Worker`) builds the Job from `BundleBuildOptions`, watches it
to completion, and only then flips `active.json` — so a failed generate/build/validate leaves
the previously active revision serving. See `deploy/kubernetes/overlays/prod/biller-experience.yaml`
(Job image env + `ic-bundle-build-runner` RBAC) and `Dockerfile`.

The container entrypoint is `src/cli.ts`, driven entirely by env (no flags). The Job sets:

| Env | Notes |
|---|---|
| `PAYER_DEFINITION_B64` | base64 of the experience definition JSON (decoded to `$PAYER_WORK/definition.json`) |
| `PAYER_SLUG` / `PAYER_REVISION` | route slug + revision; revision must match the config publisher's |
| `PAYER_MODE` | `deterministic` or `opus` |
| `PAYER_PUBLISH=true` | upload the built `dist/` tree |
| `PAYER_SKIP_ACTIVE=true` | do **not** write `active.json` — the Worker owns the atomic flip |
| `PAYER_STORAGE_ENDPOINT` / `PAYER_CONTAINER` | blob target (auth via workload identity) |
| `PAYER_PWA_DIR` | set to the PWA baked into the image (`/app/frontends/Pronto.BillerPayments.Pwa`) |
| `PAYER_WORK` / `PAYER_ARTIFACTS` | writable scratch (`/work/...`, an emptyDir) |

`--definition`/`PAYER_DEFINITION_PATH` (a file) is an alternative to `PAYER_DEFINITION_B64`;
CLI flags override env when both are present.

## Environment

- Node ≥ 22.12 (matches the PWA toolchain / vite 8).
- The PWA at `../Pronto.BillerPayments.Pwa` must have `node_modules` installed; the builder
  stages a copy and reuses that toolchain.
- Opus: `FOUNDRY_ENDPOINT` (+ `FOUNDRY_OPUS_DEPLOYMENT`) or `FOUNDRY_CHAT_URL`; auth via
  `DefaultAzureCredential` or `FOUNDRY_API_KEY`.
- Publish: auth via `DefaultAzureCredential` with `Storage Blob Data Contributor`.
