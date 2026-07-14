# Smoke tests

`smoke-test.sh` verifies the deployed Pronto services are live and reachable. Three layers:

1. **Deployment readiness** (`kubectl`) — every expected Deployment has all replicas available,
   and the gateway has a public LoadBalancer IP.
2. **Gateway reachability** (HTTP) — each service answers through the public gateway. POST-only
   services (`/payments`, `/payers`) are expected to return `405` on a GET (proves the service
   answered; the method just isn't allowed).
3. **Functional** (HTTP, read-only) — the Invoice lookup endpoint (`GET .../invoices?account_number=`)
   returns a valid `200` with an `invoices` array. Intentionally a read, not the seed POST, so the
   suite writes no data and is safe to run on a schedule. The seed/write path is covered by the
   `Pronto.Invoice.Api` unit tests.

Exit code is `0` when everything passes, non-zero otherwise — so it drops straight into CI or a cron.

## Run

```bash
deploy/smoke/smoke-test.sh
```

Requires `curl`, `jq`, and (for layer 1) `kubectl` with cluster credentials
(`az aks get-credentials -g rg-ic-hack -n aks-ic-hack`).

## Config (env vars)

| Var | Default | Purpose |
| --- | --- | --- |
| `BASE_URL` | `http://ic-hack.eastus2.cloudapp.azure.com` | Public gateway base URL |
| `HTTP_TIMEOUT` | `10` | Per-request timeout (seconds) |
| `SKIP_KUBECTL` | `0` | Set `1` to skip layer 1 when cluster creds aren't available (HTTP-only run) |
