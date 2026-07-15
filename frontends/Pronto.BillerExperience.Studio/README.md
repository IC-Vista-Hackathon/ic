# Pronto Biller Experience Studio

Chat-first biller onboarding, review, preview, approval, and publication UI.

```powershell
npm install
npm run dev
```

Set `VITE_API_URL` to override the default `http://localhost:5000` API.

## Deployment image flow

The Studio is deployed strictly source → CI image → SHA-pinned rollout, identical to the
PWA and the rest of the platform — no image is ever built or pushed by hand. On a PR (nonprod)
and on merge to `main` (prod), `.github/workflows/build-images.yml` server-side builds this
folder's `Dockerfile` with `az acr build` and pushes it to
`acrichackjk4zmntatjem4.azurecr.io/ic-biller-experience-studio` tagged with the immutable git
SHA (`github.event.pull_request.head.sha` for nonprod, `github.sha` for prod). The deploy jobs
then `sed` the `newTag: latest` placeholder in the environment's `kustomization.yaml` to that
same SHA before `kubectl apply -k`, so the running Deployment always references
`ic-biller-experience-studio:<merged-sha>` — the `:latest` in `overlays/*/*.yaml` is only a
kustomize placeholder and is never deployed. The result is verifiable: the SHA tag on the
running pod resolves to the ACR manifest digest built from that commit, and the static assets
served at `/studio/` (Vite emits content-hashed filenames like `assets/index-<hash>.js`) are
byte-identical to `npm run build` output for the same source.

The original bundled prototype remains in `design/ui.html` as a visual reference. This application
uses its Pronto design tokens while correcting forced enrollment, compliance claims, and the
wizard-first interaction model.

## Browser observability

On boot the Studio fetches `/api/public/telemetry` — the same runtime-config endpoint the payer PWA
uses (`TelemetryController.cs`), since the Studio is same-origin behind the gateway — and, when a
connection string is present, starts the Application Insights browser SDK (`src/insights.ts`).
When it's absent (e.g. plain local dev) telemetry silently stays off.

Only allowlisted semantic onboarding-funnel events leave the page, and only after passing the
strict allowlist in `src/telemetryPolicy.ts`: session started, onboarding started, chat message
sent (a count only — never the text), draft generated, preview opened (device), validation result
(outcome), checklist step completed (step), publish requested/completed/failed (error category),
and purchase started/completed. Every property key and value is validated against fixed enums,
booleans, or id shapes — biller display names, websites/URLs typed, chat text and agent responses,
emails, any `BillerExperienceDefinition` content, amounts, and raw error text have no allowlisted
slot and are stripped both in `trackEvent` and again in a telemetry initializer inside the SDK
pipeline. All of the SDK's automatic collection (fetch/ajax, exceptions, cookies, route tracking)
is disabled.

Each onboarding session carries a random, non-PII `flow_id` (sessionStorage) plus a page-load
`trace_id`, and events may carry the internal `biller_id` tenant uuid, so browser events join
server-side request telemetry in Application Insights:

```kusto
customEvents | where name startswith "studio." | project timestamp, name, customDimensions
```

`npm test` runs the vitest suite covering the allowlist and client bootstrap;
`tests/browser-smoke/telemetry-smoke.mjs` (TARGET_PATH=/studio/ EXPECTED_EVENT=studio.session_started)
verifies end-to-end delivery against a deployed environment (wired into the nonprod deploy workflow).
