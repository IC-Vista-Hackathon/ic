# Pronto Biller Payments PWA

Configuration-driven customer payment shell. It contains no Pronto customer-facing branding.

```powershell
npm install
npm run dev
```

The shared renderer extracts the biller slug from `/pay/{slug}/` and loads
`/api/public/experiences/{slug}`. Set `VITE_CONFIG_URL=/config.json` for standalone local UI work.
Invoice lookup and payment execution use a typed demo adapter until the supporting services
implement the documented contracts.

## Browser observability

On boot the app fetches `/api/public/telemetry` (served by the Biller Experience API from its
`APPLICATIONINSIGHTS_CONNECTION_STRING` env var) and, when a connection string is present, starts
the Application Insights browser SDK (`src/insights.ts`). When it's absent — e.g. plain local
dev — telemetry silently stays off.

Only semantic custom events leave the page, and only after passing the strict allowlist in
`src/telemetryPolicy.ts`: bill lookup outcome, payment method selected, review opened, payment
submitted/completed/failed, AutoPay/Paperless changes, preferences saved, session started. Every
property key and value is validated against fixed enums, booleans, or id shapes — account numbers,
names, emails, amounts, receipt/payment ids, and raw error text have no allowlisted slot and are
stripped both in `trackEvent` and again in a telemetry initializer inside the SDK pipeline. All of
the SDK's automatic collection (fetch/ajax, exceptions, cookies, route tracking) is disabled.

Each payer session carries a random, non-PII `flow_id` (sessionStorage) plus the page-load
`trace_id` already used for W3C `traceparent` propagation to the APIs, so browser events join
server-side request telemetry in Application Insights:

```kusto
customEvents | where name startswith "pwa." | project timestamp, name, customDimensions
```

`npm test` runs the vitest suite covering the allowlist and client bootstrap;
`tests/browser-smoke/telemetry-smoke.mjs` verifies end-to-end delivery against a deployed
environment (wired into the nonprod deploy workflow).
