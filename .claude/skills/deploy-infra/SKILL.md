---
name: deploy-infra
description: Validate and deploy/update the hackathon sandbox Bicep infra (Cosmos DB, AI Foundry, AKS, ACR) under infra/bicep. Use when asked to deploy, redeploy, update, or provision the Pronto hackathon Azure infra, or to check what a Bicep change would do before applying it.
---

# Deploy infra

Deploys `infra/bicep/main.bicep` (subscription-scope deployment) to the hackathon sandbox. See
`infra/bicep/README.md` for what this provisions and its known simplifications.

## Target subscription

This must run against `poc-vista-hackathon` (`ca64adec-b195-49fd-a782-15553708c07c`), not
whatever subscription happens to be current in the shell:

```sh
az account set --subscription ca64adec-b195-49fd-a782-15553708c07c
az account show --query "{name:name, id:id}" -o table   # confirm before deploying
```

## 1. Validate first

Always build the Bicep before deploying — catches syntax/type errors without touching Azure:

```sh
cd infra/bicep && az bicep build --file main.bicep --stdout > /dev/null
```

A non-zero exit or stderr output means the template doesn't compile — fix it before continuing.

## 2. Dry run (recommended before any real apply)

`--what-if` shows exactly what will change without touching resources. Run this and read the
output before every apply that isn't a first-time deploy:

```sh
az deployment sub create --name ic-hack --location eastus2 \
  --template-file main.bicep --what-if
```

Look for unexpected `Delete` or `Modify` on resources that should be untouched (e.g. Cosmos DB —
recreation would lose data even though this is a hackathon sandbox).

## 3. Apply

```sh
az deployment sub create --name ic-hack --location eastus2 --template-file main.bicep
```

Bicep/ARM incremental deployment is idempotent — re-running this with no template changes is a
no-op. This is also how updates are applied: change the `.bicep` files, then re-run the same
command.

## After deploy

- `az aks get-credentials --resource-group rg-ic-hack --name aks-ic-hack` for `kubectl` access.
- Workloads must run under namespace `ic`, service account `ic-workload` (or whatever
  `-p workloadNamespace=... workloadServiceAccountName=...` overrides were passed) to pick up the
  federated workload identity.
- Push images to the `acrLoginServer` deployment output — see the `build-and-push-image` skill.

## Known simplifications (don't "fix" these without checking with the user first)

Per `infra/bicep/README.md`: public AKS API server, public Cosmos/AI Foundry endpoints, `kubenet`
networking, no ingress controller, no Key Vault, single environment with no plan/apply pipeline.
This is the hackathon-fast path, not the hardened "Phase 6" provisioning described in the root
`README.md`'s delivery plan.
