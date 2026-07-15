# Observability dashboards & alerts

Checked-in observability artifacts for the hackathon sandbox (`rg-ic-hack`, subscription
`poc-vista-hackathon` / `ca64adec-b195-49fd-a782-15553708c07c`):

- **Grafana dashboards** (`dashboards/*.json`) — imported into the existing Azure Managed Grafana
  (`graf-ic-hack`). Managed Grafana has no ARM resource for dashboard *content*, so these ship as
  JSON and are imported (see below), not provisioned by Bicep.
- **Alert rules** — Bicep (`../bicep/modules/observability.bicep`, wired into `../bicep/main.bicep`):
  an action group plus three log-search alert rules over Application Insights (`appi-ic-hack`).

All queries use the repo's **snake_case `customEvents` conventions**. The browser funnel events
come from the PWA telemetry allowlist (`frontends/Pronto.BillerPayments.Pwa/src/telemetryPolicy.ts`,
all prefixed `pwa.`); service metrics come from server request telemetry (`cloud_RoleName` = the
OpenTelemetry `service.name` each host sets in `AddServiceDefaults`); publish metrics come from the
worker's `ic.biller.publication.*` meters.

## Dashboards

| File | Panels |
|---|---|
| `dashboards/payer-funnel.json` | Payer funnel `session_started → bill_lookup(found) → payment_method_selected → review_opened → payment_submitted → payment_completed`; payment failure rate; AutoPay/Paperless opt-in rates; failures by `error_category`. |
| `dashboards/service-health.json` | Per-API p95 latency and error rate (`requests` by `cloud_RoleName`); publish pipeline duration; publish outcomes (ready vs failed). |
| `dashboards/foundry-orchestration.json` | Foundry calls by real agent ID and outcome; invocation latency; API-to-agent distributed trace evidence. |
| `dashboards/mcp-service-router.json` | MCP tool success rate; p50/p95 latency; failures by coarse category; capability authorization denials. |

Both dashboards declare two template variables so they import cleanly into any Grafana:

- `datasource` — pick the Azure Monitor data source (Managed Grafana auto-provisions one).
- `appInsights` — the Application Insights **resource id** the KQL runs against; defaults to
  `appi-ic-hack`.

The queries are scoped to the Application Insights component, so they use the classic table names
(`customEvents`, `requests`, `customMetrics`). If you instead point a query at the Log Analytics
workspace (`log-ic-hack`), rename tables to the `App*` equivalents (`AppEvents`, `AppRequests`,
`AppMetrics`).

### Publish pipeline duration — request-to-worker-ready

The `service-health` "Publish pipeline duration" panel currently graphs the **worker-side**
processing time (`ic.biller.publication.duration`, claim → ready), which is on `main` today. The
full **request-to-worker-ready** measurement needs publish-lifecycle `customEvents` linked by a
shared `deployment_id` (a follow-up change). The exact KQL for that is kept as a comment at the top
of the panel's query so it can be swapped in once those trace/link fields land — no dashboard
restructuring required.

### Importing a dashboard

**CD is the primary path.** `.github/workflows/deploy-dashboards.yml` imports every
`dashboards/*.json` into `graf-ic-hack` on any push to `main` that touches a dashboard JSON (and
on-demand via **Actions → Deploy Grafana dashboards → Run workflow**). It authenticates as the CD
service principal (`AZURE_CREDENTIALS`), imports with `--overwrite` (idempotent — a no-op merge
that doesn't touch a dashboard doesn't run it), and then verifies both dashboard titles are present,
failing the job otherwise. No human needs a Grafana role — just edit a JSON and merge.

The workflow uses the `amg` az extension, which handles Grafana's Entra ID auth itself:

```sh
az extension add -n amg --only-show-errors
for f in infra/grafana/dashboards/*.json; do
  az grafana dashboard import -n graf-ic-hack -g rg-ic-hack --definition "$f" --overwrite
done
```

If the CD service principal lacks a Grafana role the import returns 401/403 and the job fails with
an explicit message to grant it **Grafana Editor** on the instance (`az role assignment create
--assignee <sp-object-id> --role "Grafana Editor" --scope <grafana-resource-id>`).

**Manual fallback** (only if you can't use CD — e.g. iterating locally). Grafana UI: **Dashboards →
New → Import → Upload JSON file**, then select the Azure Monitor data source when prompted. Or via
the Grafana API:

```sh
GRAFANA=$(az deployment sub show --name ic-hack --query properties.outputs.grafanaEndpoint.value -o tsv)
TOKEN=$(az account get-access-token --resource ce34e7e5-485f-4d76-964f-b3d2b16d1e4f --query accessToken -o tsv)
for f in infra/grafana/dashboards/*.json; do
  jq -n --slurpfile d "$f" '{dashboard: $d[0], overwrite: true}' \
    | curl -s -X POST "$GRAFANA/api/dashboards/db" \
        -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d @- ;
done
```

(For the manual path you need the Grafana Admin/Editor role on `graf-ic-hack`; grant yourself if you
weren't the deployer. `ce34e7e5-…` is the Azure Managed Grafana first-party app id used for AAD
tokens.)

## Alerts

`../bicep/modules/observability.bicep` creates action group `ag-ic-hack-observability` and three
`Microsoft.Insights/scheduledQueryRules`:

| Rule | Fires when | Window / freq |
|---|---|---|
| `ic-hack-pwa-payment-failed-spike` | `pwa.payment_failed` count > `paymentFailedThreshold` (default 5) | 15m / 5m |
| `ic-hack-publish-failures` | any `ic.biller.publication.results` with `outcome == "failed"` | 15m / 5m |
| `ic-hack-telemetry-silence` | at least `telemetrySilenceMinRequests` (default 20) non-health `requests` are flowing but zero `customEvents` for the lookback window (default 1h) | 1h / 15m |

The telemetry-silence rule excludes health/readiness probe requests (`url has "/health/"` or `name has "health"`) and requires a minimum non-health request count so it doesn't false-fire on probe-only traffic during genuine off-hours.

The action group is created **with no receivers** unless you pass an email. Add one at deploy time:

```sh
az deployment sub create --name ic-hack --location eastus2 \
  --template-file infra/bicep/main.bicep \
  --parameters observabilityAlertEmailAddress='oncall@example.com'
```

Set `deployObservabilityAlerts=false` to skip the alerts entirely.

> ⚠️ **`observabilityAlertEmailAddress` is not defaulted in `main.bicep` (empty string).** The
> action group's email receiver is *declarative* — any `az deployment sub create` of `main.bicep`
> that omits this parameter re-creates `ag-ic-hack-observability` with **no receivers**, silently
> wiping any address added out-of-band (e.g. a receiver added by hand in the portal). Always pass
> `observabilityAlertEmailAddress=<address>` on every infra (re)deploy, or the alerts will fire into
> a void. If/when infra deploys move to CD, that pipeline must supply this parameter (e.g. from a
> repo/environment variable or secret).

## Deploy / apply status

The alert Bicep is validated (`az bicep build`) and what-if'd against the sandbox. Follow the repo
`deploy-infra` skill / `infra/bicep/README.md` to apply — it is purely additive (a new action group
and three alert rules; no changes to existing resources):

```sh
az account set --subscription ca64adec-b195-49fd-a782-15553708c07c
az deployment sub create --name ic-hack --location eastus2 \
  --template-file infra/bicep/main.bicep --what-if      # review, then drop --what-if to apply
```
