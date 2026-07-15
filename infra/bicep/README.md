# Infra

Bicep for the hackathon sandbox: Cosmos DB (containers per [design/entities.md](../../design/entities.md)),
an AI Foundry account + project, and an AKS cluster to run [services](../../services/) and [agents](../../agents/).

Standalone sandbox subscription, not Pronto's landing zone — no ADO pipeline, no Terraform,
just plain Bicep deployed via `az`.

This is the fast, always-available sandbox path — day-to-day hackathon deploys, public endpoints,
no Key Vault. Azure-native observability (Application Insights, Azure Monitor managed Prometheus,
Azure Managed Grafana) is included at hackathon scope — see "What this creates" below. It is not
the "Phase 6" hardened provisioning (Key Vault, private endpoints) described in the root
[README.md](../../README.md)'s delivery plan; that's a separate, later effort layered on top of
this once the foundation phases land.

## Deploy

```sh
az login
az account set --subscription <sandbox-subscription-id>
az deployment sub create \
  --name ic-hack \
  --location eastus2 \
  --template-file main.bicep \
  --parameters aksDeploymentPrincipalIds='["<github-deploy-service-principal-object-id>"]'
```

The authenticated MCP project connection is fail-closed while the checked-in kgateway listener
is HTTP-only: `mcpConnectionEnabled` currently permits only `false`. Add the planned TLS ingress,
change the public endpoint to HTTPS, and then remove that restriction before provisioning an MCP
API key.

The module outputs the reviewed MCP tool definition for agent-version provisioning. It allowlists
only `get_goal_context` and `append_context`. Agent versions are Foundry data-plane objects, so ARM
Bicep creates their authenticated project connection and publishes the tool definition, while the
API's opt-in `FoundryAgentReconciler` creates immutable versions from `agents/*/instructions.md`,
attaches that definition to each approved agent, and reads the version back to verify the MCP
attachment. Enable `BillerExperience__AgentProvisioning__Enabled=true` only after the MCP connection
has been provisioned; reconciliation fails closed if the latest version lacks the reviewed tool.

Re-run the same command to apply changes; Bicep is idempotent (ARM incremental deployment).

## What this creates

| Resource | Purpose |
|---|---|
| Resource group `rg-ic-hack` | Everything below lives here |
| Log Analytics workspace | AKS Container Insights |
| ACR (Basic) | Single registry, no promotion tiers |
| Storage account (Standard_LRS, StorageV2) + `payer-experiences` blob container | Holds every biller's immutable experience artifacts and active pointer. The publisher identity gets `Storage Blob Data Contributor`; the API workload identity gets `Storage Blob Data Reader`; optional service principals can receive contributor/reader access through `payerExperienceBlobContributorPrincipalIds`/`payerExperienceBlobReaderPrincipalIds`; keys and anonymous access are disabled |
| Cosmos DB (serverless), two accounts | `cosmos-ic-hack-<suffix>` (prod) and `cosmos-ic-hack-nonprod-<suffix>` (per-PR nonprod) — same containers per entities.md, partitioned `/biller_id` (`/id` for `billers`), so nonprod smoke tests never touch prod data |
| AI Foundry account + project | Hosts agents and an optional authenticated `ic-shared-context-mcp` project connection (services.md's "AI Foundry" plane) |
| AKS (2-4 node autoscale, kubenet, managed Entra ID + Azure RBAC) | Runs services + agents; optional `aksDeploymentPrincipalIds` receive Cluster User + RBAC Writer instead of administrator credentials |
| User-assigned managed identities | `ic-workload` authenticates to Cosmos, AI Foundry, and Blob read; `biller-publisher` authenticates to Cosmos and Blob write with no secrets |
| Application Insights (workspace-based, on `log-ic-hack`) | Correlated traces/logs for services using the Azure Monitor OpenTelemetry Distro — just needs the `appInsightsConnectionString` output, no in-cluster OTEL collector |
| Azure Monitor workspace | Metrics backend for Azure Monitor managed Prometheus |
| AKS managed Prometheus (`azureMonitorProfile.metrics` + DCE/DCR/DCRA) | Scrapes Kubernetes + `kube-state-metrics`; app metrics can be added later by exposing a Prometheus-format `/metrics` endpoint (OTEL's Prometheus exporter) — no scrape-config wiring included yet |
| Azure Managed Grafana | Dashboards over the Monitor workspace's Prometheus data; granted `Monitoring Reader` on the resource group (Bicep can't scope a role assignment directly to a `Microsoft.Monitor/accounts` resource) |

## After deploy

- `az aks get-credentials --resource-group rg-ic-hack --name aks-ic-hack` to get `kubectl` access.
- The deploy workflows use the Entra/Azure RBAC identity supplied through
  `aksDeploymentPrincipalIds` and never request the AKS `system:masters` administrator certificate.
- Deployed workloads must run under namespace `ic`, service account `ic-workload` (or pass
  different values via `-p workloadNamespace=... workloadServiceAccountName=...`) to pick up the
  federated identity — annotate that service account with the workload identity's client ID
  (`workloadIdentityClientId` output) per [AKS Workload Identity](https://learn.microsoft.com/azure/aks/workload-identity-overview).
- The publication worker runs as `biller-publisher`; annotate it with the
  `publisherIdentityClientId` output. It intentionally receives no AI Foundry or Kubernetes
  mutation permissions.
- Push images to the `acrLoginServer` output; AKS already has `AcrPull` on it.
- Point services' Azure Monitor OpenTelemetry Distro at the `appInsightsConnectionString` output.
- Open the `grafanaEndpoint` output to view dashboards (sign in with Entra ID; grant yourself the
  Grafana Admin role on the Grafana resource if you weren't the deployer).
- **Not yet wired into `.env.example`** — these outputs (`appInsightsConnectionString`,
  `grafanaEndpoint`) should be added there in a follow-up once this and the `.env.example`-owning
  change both land, to avoid conflicting edits.

## Known simplifications (hackathon-shaped, not production-shaped)

- Public AKS API server and public Cosmos/AI Foundry endpoints — no private networking/VNet.
- `kubenet` networking — fine at this scale; move to Azure CNI overlay + a real VNet before this
  becomes more than a hackathon cluster.
- No ingress controller included yet — install nginx-ingress (or similar) once there's a service
  to expose.
- Single environment, no plan/apply pipeline — re-running `az deployment sub create` is the
  whole workflow.
