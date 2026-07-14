# Kubernetes

Shared namespace, RBAC, network policy, and local development manifests will live here.

Raw YAML for now — static objects, not enough variation yet to justify Helm/Kustomize.

| File | Creates |
|---|---|
| `biller-sites-namespace.yaml` | the `biller-sites` namespace — where all `biller-{slug}` workloads (Deployment, Service, ConfigMap, HTTPRoute) are published |
| `biller-publisher-service-account.yaml` | `biller-publisher` service account in the `ic` namespace, dedicated to `IC.BillerExperience.Worker` |
| `biller-publisher-role.yaml` | a `Role` + `RoleBinding` in `biller-sites` granting `biller-publisher` get/list/watch/create/update/patch/delete on Deployments (apps), Services/ConfigMaps (core), and HTTPRoutes (gateway.networking.k8s.io) only |

`biller-publisher` is intentionally separate from `ic-workload` (the workload-identity service
account used for Cosmos/AI Foundry auth): the publishing worker needs to mutate Kubernetes
resources in `biller-sites`, and nothing else in the `ic` namespace should inherit that ability.
The `RoleBinding` subject references the `ic`-namespace service account from a `Role`/`RoleBinding`
that live in `biller-sites` — a pod's `serviceAccountName` must be in its own namespace, but a
`RoleBinding`'s subject may name a `ServiceAccount` from any namespace, while the permissions it
grants stay confined to the `RoleBinding`'s own namespace. This keeps the worker's access scoped to
exactly the resource types the AKS publication model requires, with no cluster-admin and no access
outside `biller-sites`.

Apply via `az aks command invoke` (this sandbox can't reach the AKS API server directly):

```sh
az aks command invoke -g rg-ic-hack -n aks-ic-hack \
  --command "kubectl apply -f biller-sites-namespace.yaml -f biller-publisher-service-account.yaml -f biller-publisher-role.yaml" \
  --file biller-sites-namespace.yaml --file biller-publisher-service-account.yaml --file biller-publisher-role.yaml
```
