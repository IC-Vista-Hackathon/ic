# IC Biller Studio

IC Biller Studio is an agent-assisted onboarding and experience-publishing platform for billers.
A biller describes its brand and payment experience through chat, previews and approves the result,
and receives an installable, fully branded PWA running in its own Kubernetes Deployment. Customers
remain inside the biller's branded experience and existing InvoiceCloud payment rails remain
unchanged.

The disruption is onboarding speed and customization, not money movement.

## Status

Phases 2 through 4 now provide a runnable local vertical slice: versioned controller contracts,
agent-assisted onboarding with a deterministic fallback, Cosmos and in-memory repositories,
approval and idempotent publication requests, the Biller Studio, and a configuration-driven payer
PWA. Phase 5 will turn an accepted publication request into a healthy AKS deployment.

The documents under [`design/`](design/README.md) came from the original `main` branch and remain
the source of truth for supporting service responsibilities, entities, REST behavior, and agent
boundaries. This README and `IC.slnx` are the source of truth for repository and solution structure.
Where the documents use conceptual names such as "Biller Configuration Service," the mapping below
defines the concrete .NET project that implements that capability.

## Product and component names

| Concern | Name | Runtime name |
| --- | --- | --- |
| Product | IC Biller Studio | — |
| Agentic experience API | `IC.BillerExperience.Api` | `ic-biller-experience-api` |
| Publishing worker | `IC.BillerExperience.Worker` | `ic-biller-experience-worker` |
| Orchestration library | `IC.Agentic.Orchestration` | — |
| Public contracts | `IC.BillerExperience.Contracts` | — |
| Customer application | `IC.BillerPayments.Pwa` | `biller-{slug}` |
| Operational database | `ic-biller-experience` | — |

The service is named after the business capability rather than the implementation. Agentic
orchestration can evolve without renaming the API used by billers and other IC systems.

## Architecture

```text
Biller
  │ chat + preview + approval
  ▼
IC Biller Studio
  │ HTTPS / server-sent events
  ▼
IC.BillerExperience.Api
  │
  ├── IC.Agentic.Orchestration
  │     Discover → Draft → Validate → Preview → Approve
  │
  ├── Azure Cosmos DB for NoSQL
  │     billers, sessions, revisions, checkpoints, deployments
  │
  └── durable publication request
          ▼
IC.BillerExperience.Worker
  │ fixed, validated Kubernetes operations
  ▼
AKS / biller-sites namespace
  │
  ├── Deployment/biller-{slug}
  ├── ConfigMap/biller-{slug}-r{revision}
  ├── Service/biller-{slug}
  └── HTTPRoute/biller-{slug}
          │
          ▼
Branded installable PWA
          │
          ▼
Existing InvoiceCloud payment APIs and rails
```

The generated artifact is a typed, versioned `BillerExperienceDefinition`. Agents may generate
content and configuration, but they may not generate executable application code, container build
instructions, shell commands, or raw Kubernetes manifests. Publication uses vetted templates and
an immutable PWA image.

### Capability ownership

| Documented capability | Concrete project/location | Ownership |
| --- | --- | --- |
| Biller Configuration Service | `services/IC.BillerExperience.Api` | Biller Experience |
| Deployment Service | `services/IC.BillerExperience.Worker` | Biller Experience |
| Biller Onboarding Experience | `frontends/IC.BillerExperience.Studio` | Biller Experience |
| Payer Experience | `frontends/IC.BillerPayments.Pwa` | Biller Experience |
| Invoice Service | future `services/IC.Invoice.Api` | Supporting service; follow `design/` |
| Payment Service | future `services/IC.Payment.Api` | Supporting service; follow `design/` |
| Payer Account Service | future `services/IC.PayerAccount.Api` | Supporting service; follow `design/` |
| Notification Service | future `services/IC.Notification.Worker` | Stretch; follow `design/` |
| AI Foundry agent definitions | `agents/` | Follow `design/services.md` |

Supporting services retain the behavior documented on `main`, including integer-cent money values,
Cosmos tenant partitioning, deterministic ownership of persistence and payment actions, and explicit
human confirmation before the Execution Agent can initiate a payment. The Biller Experience only
configures and invokes those capabilities; it does not absorb their data or money-movement logic.

## Repository layout

```text
ic/
├── IC.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── contracts/
│   ├── IC.Contracts.slnx
│   └── IC.BillerExperience.Contracts/
├── libraries/
│   └── IC.Agentic.Orchestration/
├── services/
│   ├── IC.BillerExperience.Api/
│   └── IC.BillerExperience.Worker/
├── frontends/
│   ├── IC.BillerExperience.Studio/
│   └── IC.BillerPayments.Pwa/
├── deploy/
│   ├── helm/
│   └── kubernetes/
├── infra/
│   └── bicep/
└── tests/
```

`contracts` contains transport contracts only. Persistence entities, Kubernetes SDK types, and
Microsoft Agent Framework types must never become part of those public contracts.

`libraries/IC.Agentic.Orchestration` owns IC's framework-neutral orchestration API. Microsoft Agent
Framework can be used internally, but consumers depend on IC abstractions.

`services` contains independently deployable .NET 10 workloads. Frontends and infrastructure are
kept separate from service implementation.

`design` preserves the existing system design and capability boundaries. `agents` contains the
AI Foundry agent definitions described there. New supporting services use the `IC.<Capability>.*`
project naming and are added to `IC.slnx`; they are not introduced as a competing repository layout.

## Onboarding workflow

The production workflow is typed, checkpointed, resumable, and observable:

1. Create the biller and onboarding session.
2. Collect required brand, support, legal, PWA, and payment-capability information.
3. Detect missing or conflicting information.
4. Produce a structured experience draft.
5. Validate the schema, policy, accessibility, and supported payment references.
6. Render a live preview.
7. Wait for explicit biller approval.
8. persist an immutable experience revision.
9. Request publication idempotently using biller ID and revision.
10. Apply fixed Kubernetes resources and wait for pod readiness.
11. Smoke-test the published route and mark the revision active.

An API or worker restart must not lose a workflow. Every step records its checkpoint and can be
retried without duplicating the deployment.

## Contracts

Contracts are versioned under `IC.BillerExperience.Contracts/V1` and grouped by capability:

- `Billers`: identity, brand, support, and existing payment-rail references.
- `Onboarding`: sessions and biller messages.
- `Experiences`: editable definitions, immutable revisions, approval, and publication.
- `Deployments`: publication status and failure information.
- `Events`: business events emitted by the onboarding and publication processes.

`BillerExperienceDefinition` is the contract between agent-assisted onboarding, preview, storage,
and the customer PWA. It contains a schema version, brand tokens, content, PWA configuration,
support/legal links, and references to enabled existing payment capabilities. It never contains
payment credentials.

Supporting-service wire behavior remains defined in [`design/contracts.md`](design/contracts.md).
As those services are implemented, each receives its own versioned project under `contracts/` and
is included in `contracts/IC.Contracts.slnx`; supporting-service DTOs do not get folded into
`IC.BillerExperience.Contracts`.

## Orchestration library

`IC.Agentic.Orchestration` replaces the prototype's in-memory orchestration mode switchboard with
small, typed seams:

- `IOrchestrationWorkflow<TInput,TOutput>` defines a workflow.
- `IOrchestrationStep<TInput,TOutput>` defines one typed unit of work.
- `IOrchestrationRunner` executes workflows and owns cross-cutting telemetry.
- `IOrchestrationStateStore` will persist checkpoints and resumable state.
- agent, tool-policy, structured-output, and human-approval adapters are added behind these seams.

The biller workflow is deterministic. Models help interpret and synthesize; normal code validates,
authorizes, persists, deploys, and verifies.

## Persistence

Azure Cosmos DB for NoSQL is the store defined by the original design. Biller Experience follows
the existing entity-container model and adds a separate container only for orchestration state:

| Container | Partition key | Contents |
| --- | --- | --- |
| `billers` | `/id` | tenant-root `BillerAccount` documents |
| `configs` | `/biller_id` | versioned biller experience configuration |
| `deployments` | `/biller_id` | published deployment records |
| `orchestration_runs` | `/biller_id` | sessions, checkpoints, sanitized interactions, publish jobs |

Invoice, payment, purchase, payer-account, and notification containers remain owned by their
supporting services exactly as described in [`design/entities.md`](design/entities.md).

Safety requirements:

- Microsoft Entra Workload Identity and Cosmos RBAC; no database keys in pods.
- Private endpoint with public network access disabled.
- Continuous backup and point-in-time restore.
- `_etag` optimistic concurrency and transactional batches within a biller partition.
- Immutable approved/published revisions.
- TTL and redaction for interaction history.
- Application-level contract and policy validation despite the flexible document schema.

## Frontends

Two deliberately small frontends are planned:

### IC Biller Studio

- conversational onboarding
- missing-information checklist
- desktop/mobile live preview
- revision history
- explicit approve and publish action
- streaming workflow and deployment status

### IC Biller Payments PWA

- one reviewed, immutable application image
- CSS custom properties and configuration-driven composition
- web manifest and service worker
- accessible, responsive payment components
- integration only with existing InvoiceCloud payment APIs
- no InvoiceCloud customer-facing branding

Every published biller initially receives its own Kubernetes Deployment while sharing the same
vetted image. A revision-specific ConfigMap or API-delivered definition supplies branding and
content. A configuration hash on the pod template triggers safe rollouts.

## AKS publication model

All biller workloads initially live in a restricted `biller-sites` namespace. The publishing
worker receives a dedicated Kubernetes service account that can manage only the required resource
types in that namespace. It must never receive cluster-admin access.

Publication requires:

- validated DNS-safe resource names derived from a stable biller slug
- server-side apply using fixed resource templates
- immutable image digests rather than `latest`
- startup, readiness, and liveness probes
- resource requests and limits
- default-deny network policy and restricted pod security
- rollout timeout, smoke test, and failure recording
- idempotency on `billerId + revision`
- retention of the preceding revision for rollback

The first release uses one replica per published biller. We will measure pod count, utilization,
and operational cost before choosing scale-to-zero or a shared multi-tenant data plane.

## Azure observability

All workloads use OpenTelemetry and are observable through Azure:

- Application Insights: correlated API, workflow, agent, model, tool, storage, and publication
  traces.
- Log Analytics and Container Insights: structured application and container logs.
- Azure Monitor managed Prometheus: Kubernetes and application metrics.
- Azure Managed Grafana: platform and product dashboards.
- AKS diagnostic settings: control-plane and audit logs.
- Azure Monitor alerts and action groups: SLO and failure notification.

Every onboarding operation carries `traceId`, `billerId`, `onboardingSessionId`,
`experienceRevision`, `workflowRunId`, and `deploymentName`. Prompts, payment data, customer data,
and raw model responses are not attached to normal telemetry.

Initial product and operational metrics include onboarding completion time, validation failures,
model/tool latency and errors, token usage, publication duration and failures, rollback count, pod
readiness/restarts, Cosmos throttling, PWA availability, and payment-page request latency.

## Security boundaries

- Microsoft Entra Workload Identity for Cosmos DB, Key Vault, ACR, and Azure model access.
- Private endpoints for data and platform dependencies.
- Azure Front Door/WAF or Application Gateway in front of public traffic.
- Separate API and publishing-worker identities.
- Explicit tool allowlists for every agent.
- No shell, raw Kubernetes, arbitrary SQL, or source-generation tool exposed to an agent.
- Schema, policy, accessibility, and payment-capability validation before preview or publish.
- Explicit biller approval and immutable audit history before publication.
- Existing services remain responsible for payment authorization, processing, and settlement.

## Delivery plan

### Phase 1 — Foundation

- [x] Establish repository and .NET 10 build configuration.
- [x] Add contracts, orchestration, API, worker, frontend, deployment, infrastructure, and test
  boundaries.
- [x] Add initial versioned contracts and typed orchestration abstractions.
- [x] Add API and worker host skeletons.
- [x] Rebase onto the existing design and map its capabilities into the IC solution structure.
- [ ] Add CI, ownership, and architecture decision records.

### Phase 2 — Orchestration and persistence

- [x] Implement the biller onboarding workflow and structured model output.
- [x] Add Cosmos DB repositories, checkpoints, optimistic concurrency, and redaction boundaries.
- [x] Add cancellation, publication idempotency, policy gates, and explicit approval.
- [x] Add orchestration traces, metrics, and structured error logging.

### Phase 3 — API and Biller Studio

- [x] Implement controller-based biller/session/message/preview/approval/publication endpoints.
- [x] Stream workflow status with server-sent events.
- [x] Build the minimal chat, checklist, live preview, review, approval, and publication UI.

### Phase 4 — Customer PWA

- [x] Build the configuration-driven payment shell.
- [x] Add manifest, service worker, accessibility, responsive brand tokens, and independent
  AutoPay/paperless consent.
- [x] Add a typed payment provider boundary with a local demo provider until the documented
  supporting payment and invoice services are available.

### Phase 5 — AKS publication

- Implement fixed Kubernetes resource generation and server-side apply.
- Add readiness waiting, route smoke test, status persistence, rollback, and reconciliation.
- Add namespace RBAC, workload identity, network policy, and pod security.

### Phase 6 — Azure platform and hardening

- Provision AKS, ACR, Cosmos DB, Key Vault, Azure Monitor, Application Insights, managed
  Prometheus, and Managed Grafana with Bicep.
- Add dashboards, alerts, runbooks, audit retention, load tests, and failure exercises.

## Definition of the first vertical slice

A biller can create an onboarding session, chat until the required fields are complete, preview and
approve a generated experience, and publish it. Publication produces a new ready AKS Deployment and
a reachable branded PWA URL. Cosmos DB records the biller, approved revision, workflow checkpoints,
and deployment outcome. Application Insights contains one correlated trace from the publish request
through Kubernetes readiness. Payment movement is unchanged.

## Build

Prerequisite: .NET SDK 10.0.301 or a compatible 10.0 feature-band patch.

```powershell
dotnet restore .\IC.slnx
dotnet build .\IC.slnx --no-restore
dotnet test .\IC.slnx --no-build
```

Run the service hosts locally:

```powershell
dotnet run --project .\services\IC.BillerExperience.Api
dotnet run --project .\services\IC.BillerExperience.Worker
```

The API defaults to in-memory persistence and the deterministic model provider, so no Azure
credentials are required for a local run. Set `BillerExperience__Persistence__Provider=Cosmos` and
`BillerExperience__Persistence__CosmosEndpoint`, or set
`BillerExperience__Model__Provider=AzureAI` and `BillerExperience__Model__Endpoint`, to use the
Azure implementations with `DefaultAzureCredential`.

Run either frontend with `npm install` followed by `npm run dev` in its folder. Biller Studio uses
`http://localhost:5000` by default; override it with `VITE_API_URL`.

The API exposes `/`, `/health/live`, `/health/ready`, and controller routes rooted at `/billers`.
Logs are emitted as newline-delimited JSON for AKS/Container Insights ingestion. Application
Insights is enabled when `APPLICATIONINSIGHTS_CONNECTION_STRING` is present; traces and metrics
include the custom Biller Experience and orchestration sources without recording prompts or raw
model output.
