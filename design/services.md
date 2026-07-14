# Services & Agents

Two planes: **deterministic services** own data and money movement; **agents** (hosted in AI
Foundry) read/write through service APIs via tools. Agents never touch storage directly.

## Services

| Service | Owns | Responsibilities |
|---|---|---|
| **Biller Onboarding Experience** | — (web app) | Initial form, chat UI, live preview pane, buy button, dashboard (test payer experience, edit configuration) |
| **Biller Configuration Service** | BillerAccount, BillerConfiguration | Config CRUD (merge-patch), versioning, publish; the single write target for onboarding agents |
| **Deployment Service** | Deployment | Preview + go-live publishing; shared multi-tenant render vs isolated dedicated instance |
| **Payer Experience** | — (shared web app) | Branded portal rendered from versioned Blob artifacts; PWA (manifest, service worker, notifications); one codebase, horizontally scaled shared deployment |
| **Payer Experience API** | — (shared service) | Privately reads each biller's active versioned Blob artifact and exposes only the public-safe definition and manifest to the renderer |
| **Invoice Service** | Invoice | Seed fake invoices at onboarding; lookup by biller + account number |
| **Payment Service** | Payment, Purchase | Mock authorization, fee calculation from config, confirmations; also processes the biller's platform purchase (dogfood) |
| **Payer Account Service** | PayerAccount | Registration, preferences, linked accounts, AutoPay enrollment |
| **AI Foundry** | — | Hosts/serves all agents; tool registry, model access |
| **Notification Service** (stretch) | Notification | Email/SMS receipts and reminders |
| **Biller Site** (stretch) | — | Mock biller homepage with the payer portal embedded |

## Agents

### Onboarding side (drive Biller Configuration Service)

| Agent | Role |
|---|---|
| **Onboarding Agent** | Orchestrator — hand-holds the biller through configuration via chat; applies changes with `update_config` |
| **Biller Research Agent** | Crawls the biller's website: extracts brand (colors, logo, tone), org facts, bill types |
| **Aesthetics + Accessibility Agent** | Reviews the generated experience: contrast, WCAG basics, visual coherence; proposes fixes |
| **Biller Compliance Agent** | Checks config against policy (fee disclosure, required text, payment-method rules) before publish |

### Payer side (drive the payer portal session)

| Agent | Role |
|---|---|
| **Bill Intelligence Agent** | Finds and explains the bill: what it is, line items, due date, history |
| **Financial Planning Agent** | Plans the payment: pay now vs schedule, split, method choice given fees |
| **Policy Agent** | Knows payer preferences; gates actions (e.g. offers account creation, enforces guardrails) |
| **Execution Agent** | Actually pays — the only agent allowed to call `POST /payments`; human confirms first |

Pipeline: `Bill Intelligence → Financial Planning → Policy → Execution`. Each stage hands a
structured artifact to the next (bill summary → payment plan → approved plan → payment).

## Implementation conventions

- **API hosts use ASP.NET Core controllers, not minimal APIs.** All service hosts
  (`Pronto.BillerExperience.Api` and future `Pronto.Payment.Api`, `Pronto.PayerAccount.Api`,
  `Pronto.Invoice.Api`, …) expose their endpoints via `[ApiController]` controller classes.
  The current minimal-API placeholder endpoints in `Pronto.BillerExperience.Api/Program.cs`
  get converted when real endpoints land. Health checks (`MapHealthChecks`) stay as-is.

## Boundaries

- Only Execution Agent calls the Payment Service, and only after explicit payer confirmation.
- Publish requires a passing Compliance Agent check on the config version — enforced by the
  publish endpoint calling `run_compliance_check` itself; `compliance` is not a field agents can
  set via `update_config` (see entities.md `BillerConfiguration`).
- Isolated-tier deployments get their own instance + data partition; shared tier is row-scoped
  by `biller_id` (Cosmos partition key across all containers — see entities.md's Cosmos
  conventions).
- Completing a Purchase is a cross-service write: Payment Service marks its own Purchase `paid`,
  then calls Biller Configuration Service to advance `BillerAccount.status` to `purchased`.
