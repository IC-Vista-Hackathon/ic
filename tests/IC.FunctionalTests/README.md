# IC.FunctionalTests

Black-box functional tests that drive the **deployed** service APIs over HTTP through the
gateway. They gate every PR by running against the `ic-nonprod` environment after
`deploy-nonprod.yml` deploys the PR's images.

Unlike `IC.BillerExperience.IntegrationTests` (in-process `WebApplicationFactory`, runs in
`ci.yml` with no infra), these hit a real, Cosmos-backed deployment end to end — including the
cross-service chain (Payment → Invoice).

## Gating

The target is the `IC_FUNCTIONAL_BASE_URL` environment variable. When it is **unset**, every
test returns early (no-op) — so a plain `dotnet test IC.slnx` (e.g. the CI in-process run)
never touches a deployed environment. `deploy-nonprod.yml` sets it to the nonprod gateway URL.

## Data cleanup

Everything a test creates is cleaned up. The shared `DeployedEnvironment` fixture:

- uses a unique per-run `biller_id` (`func-<guid>`) so concurrent PR runs never collide;
- tracks every `biller_id` a test touches (including BillerExperience-generated ones);
- on teardown, calls each service's nonprod-gated `DELETE /internal/test-data?biller_id=` so the
  run leaves nothing behind in the shared nonprod Cosmos.

Those purge endpoints are disabled (404) outside nonprod — see the `Maintenance:PurgeEnabled`
gate on each service.

## Coverage

- **Health / reachability** — each service answers through the gateway.
- **Invoice** — seed → account lookup → get-by-id → unknown-account empty list.
- **Payment** — the cross-service chain (seed invoice → pay → invoice flips to `paid` → receipt),
  plus purchase.
- **PayerAccount** — register → get → update preferences, duplicate-email conflict.
- **BillerExperience** — create biller → read biller + configuration.

## Run locally against nonprod

```bash
IC_FUNCTIONAL_BASE_URL=http://ic-hack-nonprod.eastus2.cloudapp.azure.com \
  dotnet test tests/IC.FunctionalTests
```

(Cleanup only works once the nonprod deployment has the purge endpoints, i.e. after the
`purge-endpoints` change is live there.)
