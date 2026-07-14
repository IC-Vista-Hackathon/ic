# CLAUDE.md

Project-level context for Claude Code sessions working in this repo. Keep this factual and
grounded in what's actually here — update it when the repo's structure or conventions change,
don't let it drift into aspiration.

## What this is

Agent-native EBPP (electronic bill presentment and payment): a biller self-onboards through a
chat agent, previews their own branded payer portal on fake data, "buys" the platform, and
publishes it live. A payer then uses the live site, assisted by a payer-side agent team.

Three wins over today's model, where every biller gets a hand-built payer experience through a
months-long onboarding (see `design/README.md`'s "Why" for the full rationale):

1. **Fully custom payer experiences** — config-driven rendering, no shared-template constraint.
2. **Self-service onboarding** — a biller configures and goes live in minutes, unassisted,
   instead of a three-month setup engagement.
3. **A new market segment** — small billers (~$200-300 MRR) never cleared the bar for a
   three-month onboarding; self-service does, opening up a segment that wasn't viable before.

Core principles (from `design/README.md`):

- **Agents configure; services execute.** Agents emit declarative config and tool calls; all
  money movement and persistence goes through deterministic services.
- **Config is the product.** The payer experience is rendered 100% from `BillerConfiguration` —
  no per-biller code.
- **Fake rails, real flows.** Payments are mocked, but every flow (preview, purchase, go-live,
  pay) is the genuine end-to-end path.
- **Isolation is a tier.** Shared infrastructure by default; dedicated deployment is a paid
  upgrade.

## Repository layout

```text
ic/
├── IC.slnx                          solution file — new projects must be added here
├── contracts/                       versioned wire contracts (transport DTOs only)
│   └── IC.BillerExperience.Contracts/
├── libraries/                       shared .NET libraries, IC.<Capability> naming
│   └── IC.Agentic.Orchestration/    framework-neutral orchestration abstractions
├── services/                        independently deployable .NET 10 workloads
│   ├── IC.BillerExperience.Api/     Biller Configuration Service (BillerAccount, BillerConfiguration)
│   └── IC.BillerExperience.Worker/  Deployment Service (publish/reconcile to AKS)
├── frontends/                       web apps (not under services/)
│   ├── IC.BillerExperience.Studio/  Biller Onboarding Experience (chat, checklist, preview, publish)
│   └── IC.BillerPayments.Pwa/       Payer Experience — config-driven, one codebase, N deployments
├── deploy/
│   ├── helm/                        charts for control plane + generated biller workloads (not yet populated)
│   └── kubernetes/                  namespace, RBAC, gateway, and workload manifests (populated, see below)
├── infra/
│   └── bicep/                       hackathon sandbox infra: Cosmos, AI Foundry, AKS, ACR, App Insights,
│                                     managed Prometheus/Grafana, and a Storage Account for published
│                                     Payer Experience SPAs (see "Payer Experience hosting pivot" below)
├── agents/                          AI Foundry agent definitions, one subdirectory per agent
│                                    (instructions.md + tools.json each)
├── design/                          original system design — source of truth for entities,
│                                    service/agent responsibilities, and REST contracts
└── tests/                           test projects, one per service/library under test
```

Docs to read before making non-trivial changes:

- `README.md` — repo layout, product/component name mapping, architecture, delivery plan.
- `design/README.md`, `design/entities.md`, `design/services.md`, `design/contracts.md` — the
  original design; still the source of truth for entities, service/agent boundaries, and REST
  behavior, even though `README.md`/`IC.slnx` now own repo/solution structure.
- `agents/README.md`, `services/README.md`, `libraries/README.md` — how documented capabilities
  in `design/` map onto concrete `IC.<Capability>.*` projects.
- `infra/bicep/README.md` — what the hackathon sandbox infra actually provisions.

## Key architectural decisions

- **Cosmos DB for NoSQL, partitioned `/biller_id` almost everywhere.** One container per entity
  type (`billers`, `configs`, `deployments`, `payer_accounts`, `invoices`, `payments`,
  `purchases`, `notifications`, plus `orchestration-runs` for the Biller Experience). The
  `billers` container is the exception — partition key `/id`, since it's the tenant root and is
  always looked up by its own id. See `design/entities.md`'s "Cosmos conventions".
- **No foreign keys.** Cosmos can't join across containers or enforce referential integrity.
  Every `*_id` field is a denormalized reference, not an FK — if a container is commonly looked
  up without its parent in hand, the parent's key is duplicated onto it (e.g. `Payment.biller_id`
  is copied from `Invoice.biller_id`) so point reads/queries stay single-partition.
- **Workload identity, not connection strings or API keys.** Microsoft Entra Workload Identity +
  Cosmos/Cognitive Services RBAC. Pods authenticate to Cosmos (Data Contributor) and AI Foundry
  (Cognitive Services User) with a federated user-assigned managed identity — no secrets in
  pods. In-cluster this is automatic via the AKS workload identity webhook; local dev falls back
  to `az login` / a dedicated dev service principal, never to keys.
- **AI Foundry hosts the 8 agents** defined under `agents/`. Two models are deployed: `gpt-5.4`
  for agents that plan/decide or touch money/risk, `gpt-5.4-mini` for narrower single-purpose
  reads. Agents read/write only through registered service-API tools — never storage directly,
  and never raw shell, Kubernetes, or SQL. See `agents/README.md` and `design/services.md`.
- **Agents configure, deterministic services execute money movement and persistence.** Only the
  Execution Agent may call the Payment Service, and only after explicit payer confirmation.
  Publish requires a passing Compliance Agent check, enforced server-side by the publish
  endpoint — `compliance` is not agent-writable via `update_config`.
- **Contracts vs. design docs.** `contracts/IC.BillerExperience.Contracts` holds versioned
  transport DTOs for the Biller Experience capability only. Persistence entities, Kubernetes SDK
  types, and Microsoft Agent Framework types must never leak into those contracts. Invoice,
  Payment, and PayerAccount now have versioned contract projects under `contracts/` and hosts
  under `services/`; Notification keeps its wire behavior defined in `design/contracts.md` until
  it gets its own versioned project. Wire format is snake_case with lowercase string enums —
  non-Invoice hosts get it from `libraries/IC.ServiceDefaults`.
- **Payer Experience hosting pivot: shared router + Blob Storage, not one Deployment per biller.**
  The original AKS publication model (still what's implemented today — see `biller-city-of-vista`
  in `deploy/kubernetes/biller-experience.template.yaml`) gives every published biller its own
  Kubernetes Deployment/Service/HTTPRoute running the same PWA image. We're moving off that: the
  Worker instead uploads each biller's built static PWA bundle into the shared `payer-experiences`
  blob container (keyed by biller_id/slug prefix), and a single shared router workload resolves
  the biller from the request and serves the matching prefix — because these pods only ever serve
  static content, not per-biller logic, one router replaces N per-biller Deployments. The Storage
  Account (`modules/storage.bicep`) is live: `ic-workload` — the same identity already used for
  Cosmos/AI Foundry — has `Storage Blob Data Contributor` on it, covering both the Worker's writes
  and the router's reads; no separate identity per role. Still to build: the router workload
  itself and the Worker logic that builds/uploads a biller's bundle and updates publish status.
  See `design/services.md`'s Deployment Service and Payer Experience rows, and `README.md`'s "AKS
  publication model" section, both updated for this target.

## Git workflow

Feature branches + pull requests — **not** direct pushes to `main`. This is a recent, binding
convention: work happens on a `feature/<name>` branch, gets pushed, and lands via `gh pr create`
against `main`. Do not commit or push straight to `main`.

## Deployed Azure infra

- `infra/bicep/README.md` is the source of truth for what the hackathon sandbox provisions
  (Cosmos DB, AI Foundry, AKS, ACR, Log Analytics, a workload-identity-federated managed
  identity) and how to deploy/redeploy it.
- A root `.env.example` documenting live endpoint values (Cosmos endpoint, AI Foundry endpoint,
  ACR login server, AKS cluster name, the workload identity client ID) exists on branch
  `feature/env-example` but has not landed on `main` as of this writing — check whether it's
  merged before assuming it's present.
- Target subscription for this sandbox is `poc-vista-hackathon`
  (`ca64adec-b195-49fd-a782-15553708c07c`), resource group `rg-ic-hack`, region `eastus2`. This
  is a standalone hackathon sandbox subscription, not InvoiceCloud's landing zone — no ADO
  pipeline, no Terraform, plain Bicep deployed via `az`.

## Current state

All 5 services and both frontends have Dockerfiles. `deploy/kubernetes/` is populated (namespace,
RBAC, kgateway/Gateway API routing, and the unified biller-experience template) — `deploy/helm/`
is still just a placeholder README.

- `IC.BillerExperience.Api` is the most complete piece: real onboarding orchestration
  (Discover → Draft → Validate → Preview → Approve), Cosmos + in-memory persistence, deterministic
  and Azure OpenAI draft generation. Phases 1-4 of the delivery plan are done.
- `IC.Invoice.Api`, `IC.Payment.Api`, `IC.PayerAccount.Api` have real controllers/domain logic.
  `IC.Payment.Api`'s `IBillerAccountClient` is an intentional no-op stub (Biller Experience API has
  no account-status endpoint yet).
- **`IC.BillerExperience.Worker` is still a scaffold** — `PublicationWorker.cs` logs "ready" and
  does `Task.Delay(Infinite)`; it does not yet watch for publication requests or do anything.
  Phase 5 ("AKS publication") is entirely unimplemented, and is being replaced by the storage +
  router pivot described above rather than the original per-biller-Deployment design.
- `IC.BillerExperience.Studio` and `IC.BillerPayments.Pwa` are small but functional React apps.
  The PWA currently runs against a local `DemoPaymentExperienceProvider`, not the real
  Payment/Invoice/PayerAccount services.
- Test coverage is thin outside Invoice/Payment. `BillerExperience.IntegrationTests` now has
  in-process integration tests for the Invoice API (health endpoints + seed-then-lookup flow),
  added with the GitHub Actions CI/CD pipeline; `BillerExperience.Worker.Tests` remains sparse.
- The public Gateway endpoint (`ic-hack.eastus2.cloudapp.azure.com`) is live and verified.

Don't assume build/deploy tooling exists beyond what's described above — check before relying on
it.
