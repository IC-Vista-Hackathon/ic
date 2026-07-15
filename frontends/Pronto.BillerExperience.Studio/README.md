# Pronto Biller Experience Studio

Chat-first biller onboarding, review, preview, approval, and publication UI.

```powershell
npm install
npm run dev
```

Set `VITE_API_URL` to override the default `http://localhost:5000` API.

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
