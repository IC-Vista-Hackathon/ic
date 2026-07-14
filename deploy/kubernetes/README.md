# Kubernetes

Shared namespace, RBAC, network policy, and workload manifests live here.

## Control-plane services (Kustomize)

The control-plane service workloads are managed with Kustomize:

- `base/` — namespace-agnostic Deployment/Service manifests for the four stateless API
  services (`ic-biller-experience-api`, `ic-invoice-api`, `ic-payment-api`,
  `ic-payer-account-api`) plus the `ic-workload` service account. The Biller Experience
  Worker is deployed via `biller-experience.template.yaml` (it needs live Cosmos/blob +
  `ic`-namespace workload identity), so it is not part of these overlays.
- `overlays/nonprod/` — the `ic-nonprod` namespace with its own dedicated public
  kgateway `Gateway` (`ic-hack-nonprod.eastus2.cloudapp.azure.com`, a separate Azure
  LoadBalancer from prod's). Deployed on every PR; smoke tests still use
  `kubectl port-forward` (deterministic, no wait on LB/DNS provisioning).
- `overlays/prod/` — the `ic` namespace plus public kgateway `HTTPRoute`s. Deployed on
  merge to `main`.

Deploys are automated by GitHub Actions (`.github/workflows/deploy-{nonprod,prod}.yml`):
each pins every image to the commit SHA (`kustomize`/`newTag`) and runs
`kubectl apply -k deploy/kubernetes/overlays/<env>`. To apply manually:

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
| `biller-experience.template.yaml` | API, worker, Studio, shared PWA renderer, services, probes, resource controls, and Gateway API routes |
| `biller-publisher-service-account.yaml` | dedicated Azure workload identity for the artifact publisher |

The Biller Experience template is rendered at deploy time. Replace `ACR_LOGIN_SERVER`,
`IMAGE_TAG`, `COSMOS_ENDPOINT`, `AI_FOUNDRY_ENDPOINT`, `PAYER_EXPERIENCE_BLOB_ENDPOINT`,
`PUBLIC_BASE_URL`, and
`APPLICATIONINSIGHTS_CONNECTION_STRING` from the `ic-hack` subscription deployment outputs before
passing it to `kubectl apply`. A single immutable image tag identifies the release.

**Pivot in progress:** the template's `biller-city-of-vista` Deployment/Service/HTTPRoute is a
stand-in for what will become one shared **Payer Site Router** workload instead of one Deployment
per biller — published payer sites only serve static content, so per-biller compute is wasted
spend. The target: `IC.BillerExperience.Worker` uploads each biller's built static PWA bundle into
the `payer-experiences` blob container (`infra/bicep/modules/storage.bicep`, already provisioned),
keyed by biller_id/slug prefix, and the router resolves the biller per request and serves the
matching prefix behind a single `HTTPRoute`. `biller-publisher-role.yaml`'s Deployment/Service/HTTPRoute
RBAC then only applies to the isolated (paid) tier, which still gets dedicated per-biller compute.
Not yet built: the router
workload itself and the Worker's blob-publish logic — see root `README.md`'s "AKS publication
model".

Workloads that need Cosmos/AI Foundry access should run under the `ic-workload` service account
(`serviceAccountName: ic-workload` in the pod spec) to pick up the federated identity — no
connection strings or API keys.

`biller-publisher` is intentionally separate from `ic-workload`. It can claim deployment records
in Cosmos and write to the private biller-artifacts container, but has no AI Foundry or Kubernetes
mutation permissions. The API uses `ic-workload` to read active artifacts for the public renderer.

Apply via `az aks command invoke` (this sandbox can't reach the AKS API server directly — see
infra/bicep's README):

```sh
az aks command invoke -g rg-ic-hack -n aks-ic-hack \
  --command "kubectl apply -f namespace.yaml -f service-account.yaml -f biller-publisher-service-account.yaml" \
  --file namespace.yaml --file service-account.yaml \
  --file biller-publisher-service-account.yaml
```

The foundation API services (`ic-invoice-api`, `ic-payment-api`, `ic-payer-account-api`) all
persist to Cosmos DB (database `ic`, one container per entity, partition key `/biller_id`) via
`DefaultAzureCredential` — no connection strings or keys. Persistence is selected at runtime
through the `Persistence__Provider` env (`Cosmos` in-cluster, `InMemory` default for local dev);
the pods carry the `azure.workload.identity/use: "true"` label and run under `ic-workload` so the
AKS webhook injects the federated token. State is shared across replicas, so these are safe to
scale (kept at 1 for the sandbox). Their manifests live under `base/` and deploy through the
Kustomize overlays above.

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
`service.beta.kubernetes.io/azure-dns-label-name: ic-hack` annotation to that Service via
`GatewayParameters.spec.kube.service.extraAnnotations` (referenced from the Gateway through
`spec.infrastructure.parametersRef` — Gateway API's own `infrastructure.annotations` field is not
used because kgateway plumbs Service customization through its `GatewayParameters` CRD instead).
Azure's cloud-provider integration then registers the hostname directly in Azure's own DNS zone
(`*.cloudapp.azure.com`), which is authoritative and resolves immediately — no external registrar,
no cost, no propagation delay.

Resulting hostname:

```
ic-hack.eastus2.cloudapp.azure.com   ->  20.96.210.8
```

Verified two ways:

```bash
az network public-ip list -g MC_rg-ic-hack_aks-ic-hack_eastus2 \
  --query "[].{name:name, ip:ipAddress, fqdn:dnsSettings.fqdn}" -o table
# kubernetes-a31175a886f9c42f1a7c997a7fa2f750  20.96.210.8  ic-hack.eastus2.cloudapp.azure.com

curl -i http://ic-hack.eastus2.cloudapp.azure.com/
# HTTP/1.1 500 Internal Server Error
# server: envoy
```

The `500` (not a connection failure, and `server: envoy`) is expected: the placeholder
`HTTPRoute` points at a `Service` that doesn't exist. It proves the Gateway is up, its hostname is
publicly resolvable, and it's serving real HTTP traffic.

### Shared payer route

`biller-experience.template.yaml` attaches one `/pay` path-prefix route to the shared
`ic-biller-payments-pwa` service. The renderer extracts the biller slug from `/pay/{slug}/` and
loads the corresponding active artifact through the Biller Experience API. Publishing a biller
does not create or mutate Kubernetes resources.
