# Kubernetes

Shared namespace, RBAC, network policy, and workload manifests live here.

## Control-plane services (Kustomize)

The control-plane service workloads are managed with Kustomize:

- `base/` — namespace-agnostic Deployment/Service manifests for the four stateless API
  services (`ic-biller-experience-api`, `ic-invoice-api`, `ic-payment-api`,
  `ic-payer-account-api`) plus the `ic-workload` service account. `base` ships the
  in-memory `ic-biller-experience-api` (no Azure dependencies), which is what `ic-nonprod`
  runs. The Biller Experience Worker and frontends are NOT in `base` — they need live
  Cosmos/blob + `ic`-namespace workload identity, so they live only in the prod overlay.
- `overlays/nonprod/` — the `ic-nonprod` namespace with its own dedicated public
  kgateway `Gateway` (`pronto-nonprod.eastus2.cloudapp.azure.com`, a separate Azure
  LoadBalancer from prod's). Deployed on every PR; smoke tests still use
  `kubectl port-forward` (deterministic, no wait on LB/DNS provisioning).
- `overlays/prod/` — the `ic` namespace plus public kgateway `HTTPRoute`s. Adds the
  Azure-backed biller-experience stack on top of `base`: an env patch that switches
  `ic-biller-experience-api` to Cosmos + AI Foundry + blob
  (`biller-experience-api-env-patch.yaml`), and the Worker, `biller-publisher` service
  account, Studio, and shared PWA (`biller-experience.yaml`) with their `/`, `/studio`,
  and `/pay` routes. Deployed on merge to `main`. `kubectl apply -k overlays/prod` applies
  a safe baseline without telemetry or bundle-build Jobs; the deploy workflow then injects
  the App Insights connection string and SHA-pinned builder image with `kubectl set env`.
  Prod preserves
  the existing `app.kubernetes.io/name` Deployment selectors and combined
  `ic-biller-experience` route identity so updates apply in place.

Deploys are automated by GitHub Actions (`.github/workflows/deploy-{nonprod,prod}.yml`):
each pins every image to the commit SHA (`kustomize`/`newTag`) and runs
`kubectl apply -k deploy/kubernetes/overlays/<env>`. Nonprod deploys are limited to trusted
same-repository PRs after a maintainer applies the `safe-to-deploy` label, and the shared
namespace serializes those deployments. A new PR commit requires removing and reapplying the
label so approval is tied to the reviewed head SHA. The CI identity must use cluster-user
credentials plus the AKS RBAC Writer role; the workflows do not request AKS admin credentials
or permit role-binding changes. Set the `SMOKE_PUBLISHED_EXPERIENCE_SLUG` repository variable to
a stable published fixture to exercise both its public API pointer and Router bundle. To apply
manually:

```sh
kubectl apply -k deploy/kubernetes/overlays/nonprod   # or .../prod
```

Note: the AKS API server is public, so GitHub-hosted runners reach it directly with
`az aks get-credentials`. This Devin sandbox cannot (it egresses through an intercepting
proxy AKS won't trust), which is why the manual/`biller-sites` steps below use
`az aks command invoke` instead.

## Publication & platform manifests (raw YAML)

The publication-plane and platform objects below are static enough to stay as raw YAML:

| File | Creates |
|---|---|
| `base/service-account.yaml` | `ic-workload` service account, federated to `uami-ic-hack-workload` via workload identity (see `infra/bicep`); namespace set per overlay |
| `overlays/{nonprod,prod}/namespace.yaml` | the `ic-nonprod` / `ic` namespaces |
| `overlays/prod/biller-experience.yaml` | Worker, `biller-publisher` service account, Studio, shared PWA renderer, and their services (prod only) |
| `overlays/prod/biller-experience-api-env-patch.yaml` | prod-only env patch: Cosmos + AI Foundry + blob on `ic-biller-experience-api` |

The non-secret sandbox endpoints (Cosmos, AI Foundry, blob) are literals in those files,
matching `.env.example`. `APPLICATIONINSIGHTS_CONNECTION_STRING` is resolved from Azure at
deploy time and injected with `kubectl set env`, so no connection string is committed. The
bundle-builder env and all container image tags are pinned to the release SHA by the deploy
workflow.

**Pivot in progress:** published payer sites only serve static content, so the target is one
shared **Payer Site Router** workload instead of one Deployment per biller. `Pronto.BillerExperience.Worker`
uploads each biller's artifacts into the `payer-experiences` blob container
(`infra/bicep/modules/storage.bicep`, already provisioned), keyed by biller_id/slug prefix, and the
API serves the active artifact to the shared PWA. Per-biller Deployment/Service/HTTPRoute RBAC then
only applies to the isolated (paid) tier. See root `README.md`'s "AKS publication model".

Workloads that need Cosmos/AI Foundry access should run under the `ic-workload` service account
(`serviceAccountName: ic-workload` in the pod spec) to pick up the federated identity — no
connection strings or API keys.

`biller-publisher` is intentionally separate from `ic-workload`. It can claim deployment records
in Cosmos and write to the private biller-artifacts container, but has no AI Foundry or Kubernetes
mutation permissions. The API uses `ic-workload` to read active artifacts for the public renderer.

The whole stack is applied by the deploy workflows with `kubectl apply -k overlays/<env>`.
This Devin sandbox can't reach the AKS API server directly (see infra/bicep's README), so
manual applies from here go through `az aks command invoke`.

The foundation API services (`ic-invoice-api`, `ic-payment-api`, `ic-payer-account-api`) persist
to Cosmos DB (database `ic`, one container per entity, partition key `/biller_id`) via
`DefaultAzureCredential` — no connection strings or keys. Persistence is selected at runtime
through the `Persistence__Provider` env; the base defaults to `InMemory`, and **each overlay**
patches in `Provider=Cosmos`, the endpoint, and the `azure.workload.identity/use: "true"` pod
label (`overlays/{prod,nonprod}/cosmos-persistence.yaml`). The two environments point at
**separate Cosmos accounts** — prod at `cosmos-ic-hack-<suffix>`, nonprod at
`cosmos-ic-hack-nonprod-<suffix>` — so per-PR smoke tests exercise real Cosmos without touching
prod data. The shared `ic-workload` identity is federated to both `system:serviceaccount:ic:ic-workload`
and `system:serviceaccount:ic-nonprod:ic-workload` (`infra/bicep/modules/aks.bicep`) so pods in
either namespace can obtain a Cosmos token. With shared Cosmos state each env is safe to scale
(kept at 1 for the sandbox). Manifests live under `base/` and deploy through the Kustomize
overlays above.

## Gateway API / kgateway ingress

The cluster's public entry point is [kgateway](https://kgateway.dev) (formerly Gloo Gateway), the
CNCF Gateway API implementation. The shared `/pay` route (see the root `README.md` architecture
diagram) assumes a Gateway API `Gateway` is already running in the cluster — kgateway is what
provisions and reconciles it. Manifests live under
[`gateway/`](gateway/).

### What's installed

| Component | How | Namespace |
| --- | --- | --- |
| Gateway API CRDs (standard channel, v1.5.1) | `kubectl apply --server-side` from the upstream release | cluster-scoped |
| kgateway CRDs (`kgateway-crds` chart, v2.3.6) | Helm, OCI registry | `kgateway-system` |
| kgateway controller (`kgateway` chart, v2.3.6) | Helm, OCI registry | `kgateway-system` |
| `GatewayClass/kgateway` | auto-created by the kgateway Helm chart | cluster-scoped |
| `GatewayParameters/ic-hack-gw-params` | [`gateway/gateway-parameters.yaml`](gateway/gateway-parameters.yaml) | `kgateway-system` |
| `Gateway/ic-gateway` | [`gateway/gateway.yaml`](gateway/gateway.yaml) | `kgateway-system` |
| `HTTPRoute/placeholder` (proof-of-life only) | [`gateway/httproute-placeholder.yaml`](gateway/httproute-placeholder.yaml) | `kgateway-system` |

### Install

Run from a machine with `az` access to the subscription (or via `az aks command invoke` if direct
`kubectl`/`helm` access to the API server isn't available, e.g. this sandbox goes through an
intercepting proxy that AKS won't trust):

```bash
# 1. Gateway API CRDs (standard channel)
az aks command invoke -g rg-ic-hack -n aks-ic-hack --command \
  "kubectl apply --server-side -f https://github.com/kubernetes-sigs/gateway-api/releases/download/v1.5.1/standard-install.yaml"

# 2. kgateway CRDs + controller (OCI Helm charts)
az aks command invoke -g rg-ic-hack -n aks-ic-hack --command \
  "helm upgrade -i kgateway-crds oci://cr.kgateway.dev/kgateway-dev/charts/kgateway-crds --create-namespace --namespace kgateway-system --version v2.3.6 && \
   helm upgrade -i kgateway oci://cr.kgateway.dev/kgateway-dev/charts/kgateway --namespace kgateway-system --version v2.3.6"

# 3. GatewayParameters + Gateway + placeholder HTTPRoute
cd deploy/kubernetes/gateway
az aks command invoke -g rg-ic-hack -n aks-ic-hack \
  --command "kubectl apply -f gateway-parameters.yaml -f gateway.yaml -f httproute-placeholder.yaml" \
  --file gateway-parameters.yaml --file gateway.yaml --file httproute-placeholder.yaml
```

Installing the `kgateway` Helm chart auto-creates the `GatewayClass/kgateway`
(`controllerName: kgateway.dev/kgateway`); no separate GatewayClass manifest is needed.

### Public hostname (no custom DNS, no propagation wait)

`Gateway/ic-gateway` defaults to a `Service` of `type: LoadBalancer`, which gets a public Azure
Load Balancer IP. `gateway/gateway-parameters.yaml` adds the
`service.beta.kubernetes.io/azure-dns-label-name: pronto` annotation to that Service via
`GatewayParameters.spec.kube.service.extraAnnotations` (referenced from the Gateway through
`spec.infrastructure.parametersRef` — Gateway API's own `infrastructure.annotations` field is not
used because kgateway plumbs Service customization through its `GatewayParameters` CRD instead).
Azure's cloud-provider integration then registers the hostname directly in Azure's own DNS zone
(`*.cloudapp.azure.com`), which is authoritative and resolves immediately — no external registrar,
no cost, no propagation delay.

Resulting hostname:

```
pronto.eastus2.cloudapp.azure.com   ->  20.96.210.8
```

Verified two ways:

```bash
az network public-ip list -g MC_rg-ic-hack_aks-ic-hack_eastus2 \
  --query "[].{name:name, ip:ipAddress, fqdn:dnsSettings.fqdn}" -o table
# kubernetes-a31175a886f9c42f1a7c997a7fa2f750  20.96.210.8  pronto.eastus2.cloudapp.azure.com

curl -i http://pronto.eastus2.cloudapp.azure.com/
# HTTP/1.1 500 Internal Server Error
# server: envoy
```

The `500` (not a connection failure, and `server: envoy`) is expected: the placeholder
`HTTPRoute` points at a `Service` that doesn't exist. It proves the Gateway is up, its hostname is
publicly resolvable, and it's serving real HTTP traffic.

### Shared payer route

`overlays/prod/httproutes.yaml` attaches one `/pay` path-prefix route to the shared
`ic-biller-payments-pwa` service. The renderer extracts the biller slug from `/pay/{slug}/` and
loads the corresponding active artifact through the Biller Experience API. Publishing a biller
does not create or mutate Kubernetes resources.
