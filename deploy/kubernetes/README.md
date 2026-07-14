# Kubernetes

Shared namespace, RBAC, network policy, and local development manifests will live here.

Raw YAML for now — two static objects, not enough variation yet to justify Helm/Kustomize.

| File | Creates |
|---|---|
| `namespace.yaml` | the `ic` namespace |
| `service-account.yaml` | `ic-workload` service account, federated to `uami-ic-hack-workload` via workload identity (see `infra/bicep`) |

Apply via `az aks command invoke` (this sandbox can't reach the AKS API server directly — see
infra/bicep's README):

```sh
az aks command invoke -g rg-ic-hack -n aks-ic-hack \
  --command "kubectl apply -f namespace.yaml -f service-account.yaml" \
  --file namespace.yaml --file service-account.yaml
```

Workloads that need Cosmos/AI Foundry access should run under this service account
(`serviceAccountName: ic-workload` in the pod spec) to pick up the federated identity — no
connection strings or API keys.
