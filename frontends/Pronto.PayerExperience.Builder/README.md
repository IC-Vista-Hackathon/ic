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
  -> build bundle          (tsc typecheck gate + vite build, base=/pay/{slug}/)
  -> validate bundle       (scripted Playwright happy-path gate, mocked backend)
  -> publish bundle        (upload full dist/ to Blob Storage, flip active pointer last)
```

The AI only ever authors the **skin** (`src/skin/theme.css` + `src/skin/chrome.tsx` in the
PWA) — the payment flow, service calls, and money logic are stable core with fixed
`data-testid`s the gate drives. A generated bundle that fails typecheck, build, or the
Playwright happy path is rejected before it can be published.

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
