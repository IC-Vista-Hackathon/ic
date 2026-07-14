# Infra

Bicep for the hackathon sandbox: Cosmos DB (containers per [design/entities.md](../../design/entities.md)),
an AI Foundry account + project, and an AKS cluster to run [services](../../services/) and [agents](../../agents/).

Standalone sandbox subscription, not InvoiceCloud's landing zone — no ADO pipeline, no Terraform,
just plain Bicep deployed via `az`.

This is the fast, always-available sandbox path — day-to-day hackathon deploys, public endpoints,
no Key Vault. It is not the "Phase 6" hardened provisioning (Key Vault, private endpoints, Managed
Grafana/Prometheus) described in the root [README.md](../../README.md)'s delivery plan; that's a
separate, later effort layered on top of this once the foundation phases land.

## Deploy

```sh
az login
az account set --subscription <sandbox-subscription-id>
az deployment sub create \
  --name ic-hack \
  --location eastus2 \
  --template-file main.bicep
```

Re-run the same command to apply changes; Bicep is idempotent (ARM incremental deployment).

## What this creates

| Resource | Purpose |
|---|---|
| Resource group `rg-ic-hack` | Everything below lives here |
| Log Analytics workspace | AKS Container Insights |
| ACR (Basic) | Single registry, no promotion tiers |
| Cosmos DB (serverless) | Containers per entities.md, partitioned `/biller_id` (`/id` for `billers`) |
| AI Foundry account + project | Hosts agents (services.md's "AI Foundry" plane) |
| AKS (2-4 node autoscale, kubenet) | Runs services + agents |
| User-assigned managed identity | Federated to AKS via workload identity — pods authenticate to Cosmos (Data Contributor) and AI Foundry (Cognitive Services User) with no secrets |

## After deploy

- `az aks get-credentials --resource-group rg-ic-hack --name aks-ic-hack` to get `kubectl` access.
- Deployed workloads must run under namespace `ic`, service account `ic-workload` (or pass
  different values via `-p workloadNamespace=... workloadServiceAccountName=...`) to pick up the
  federated identity — annotate that service account with the workload identity's client ID
  (`workloadIdentityClientId` output) per [AKS Workload Identity](https://learn.microsoft.com/azure/aks/workload-identity-overview).
- Push images to the `acrLoginServer` output; AKS already has `AcrPull` on it.

## Known simplifications (hackathon-shaped, not production-shaped)

- Public AKS API server and public Cosmos/AI Foundry endpoints — no private networking/VNet.
- `kubenet` networking — fine at this scale; move to Azure CNI overlay + a real VNet before this
  becomes more than a hackathon cluster.
- No ingress controller included yet — install nginx-ingress (or similar) once there's a service
  to expose.
- Single environment, no plan/apply pipeline — re-running `az deployment sub create` is the
  whole workflow.
