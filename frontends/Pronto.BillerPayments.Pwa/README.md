# Pronto Biller Payments PWA

Configuration-driven customer payment shell. It contains no Pronto customer-facing branding.

```powershell
npm install
npm run dev
```

The shared renderer extracts the biller slug from `/pay/{slug}/` and loads
`/api/public/experiences/{slug}`. Set `VITE_CONFIG_URL=/config.json` for standalone local UI work.
Invoice lookup, quotes, and payment execution go through `ServicePaymentExperienceProvider`
(`src/provider.ts`), which calls the Invoice / Payment / Payer Account services.

## Payment assistant

After a bill is looked up, the app shows a "Payment assistant" panel above the manual payment
flow. It calls the Biller Experience API's `POST /billers/{billerId}/payer-chat`, which runs the
deterministic payer-side agent pipeline (Bill Intelligence → Financial Planning) and returns a
payer-facing message plus a recommended method, timing, fee, and total. The panel is advisory —
it never moves money; the payer still chooses a method and confirms below. "Use <method>" applies
the recommendation to the method selector. If the assistant call fails, the panel shows an inline
error and the manual flow stays fully usable.

## Local end-to-end demo

`../../scripts/payer-agent-demo.sh` boots the four backend services in-memory, seeds a demo biller
(`demo-water`) with invoices + a payer for account `4421`, and writes `public/config.local.json`.
Then start the PWA against that config:

```bash
../../scripts/payer-agent-demo.sh                       # boot + seed (leave running)
VITE_CONFIG_URL=/pay/config.local.json npm run dev      # in this directory
# open http://localhost:5174/pay/demo-water/ and enter account 4421
```

`vite.config.ts` proxies `/api`, `/invoices`, `/payments`, and `/payers` to those local hosts,
mirroring the production gateway prefixes. No Azure credentials are needed — the services default
to in-memory persistence and the deterministic model provider. Stop the services with
`../../scripts/payer-agent-demo.sh --stop`.

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
