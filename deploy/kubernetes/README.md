# Kubernetes

Shared namespace, RBAC, network policy, and local development manifests will live here.

## Gateway API / kgateway ingress

The cluster's public entry point is [kgateway](https://kgateway.dev) (formerly Gloo Gateway), the
CNCF Gateway API implementation. This repo's `HTTPRoute/biller-{slug}` publication step (see the
root `README.md` architecture diagram) assumes a Gateway API `Gateway` is already running in the
cluster — kgateway is what provisions and reconciles it. Manifests live under
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

### Adding a real HTTPRoute later

Once a biller Deployment/Service is published into the `biller-sites` namespace (per the root
`README.md` AKS publication model), point an `HTTPRoute` at `Gateway/ic-gateway` from that
namespace:

```yaml
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: biller-{slug}
  namespace: biller-sites
spec:
  parentRefs:
    - name: ic-gateway
      namespace: kgateway-system
  hostnames:
    - "ic-hack.eastus2.cloudapp.azure.com"   # or a future custom domain
  rules:
    - backendRefs:
        - name: biller-{slug}
          port: 8080
```

`allowedRoutes.namespaces.from: All` on the Gateway's listener already permits `HTTPRoute`s from
any namespace to attach, so no `ReferenceGrant` is needed for same-cluster Services. Delete
`gateway/httproute-placeholder.yaml` (or leave it — it only matches unclaimed paths) once real
routes exist.
