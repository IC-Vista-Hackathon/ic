#!/usr/bin/env bash
# Functional smoke tests against a deployed Pronto environment.
#
# Reaches the services in the target namespace via `kubectl port-forward` (no public
# ingress required), so it works identically for nonprod (ic-nonprod) and prod (ic).
#
# Checks readiness + liveness for every service in the target overlay, including
# the prod-only publication Worker and payer-site Router. Then runs a functional
# Invoice seed/read flow.
#
# Usage: scripts/smoke-test.sh <namespace>
set -euo pipefail

NAMESPACE="${1:?usage: smoke-test.sh <namespace>}"
LOCAL_PORT="${LOCAL_PORT:-18080}"
CURL="curl -fsS --max-time 10"

API_SERVICES=(ic-biller-experience-api ic-invoice-api ic-payment-api ic-payer-account-api)
ALL_TARGETS=(
  "svc/ic-biller-experience-api"
  "svc/ic-invoice-api"
  "svc/ic-payment-api"
  "svc/ic-payer-account-api"
)
if [[ "$NAMESPACE" == "ic" ]]; then
  API_SERVICES+=(ic-biller-experience-worker ic-payer-experience-router)
  ALL_TARGETS+=(
    "deploy/ic-biller-experience-worker"
    "svc/ic-payer-experience-router"
  )
fi

pf_pid=""
cleanup() { [[ -n "$pf_pid" ]] && kill "$pf_pid" >/dev/null 2>&1 || true; }
trap cleanup EXIT

# port-forward $1 (e.g. svc/ic-invoice-api) to localhost:$LOCAL_PORT and wait until ready
start_pf() {
  cleanup
  kubectl port-forward -n "$NAMESPACE" "$1" "${LOCAL_PORT}:8080" >/dev/null 2>&1 &
  pf_pid=$!
  for _ in $(seq 1 30); do
    if $CURL "http://127.0.0.1:${LOCAL_PORT}/health/live" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  echo "ERROR: $1 did not become reachable in namespace $NAMESPACE" >&2
  return 1
}

echo "== Waiting for rollouts in $NAMESPACE =="
for d in "${API_SERVICES[@]}"; do
  kubectl rollout status -n "$NAMESPACE" "deploy/$d" --timeout=180s
done

echo "== Health checks =="
for target in "${ALL_TARGETS[@]}"; do
  start_pf "$target"
  $CURL "http://127.0.0.1:${LOCAL_PORT}/health/ready" >/dev/null
  $CURL "http://127.0.0.1:${LOCAL_PORT}/health/live" >/dev/null
  echo "  ok: $target (ready + live)"
done

echo "== Browser telemetry configuration =="
start_pf "svc/ic-biller-experience-api"
telemetry=$($CURL "http://127.0.0.1:${LOCAL_PORT}/public/telemetry")
echo "  ${telemetry}"
# Shape check only: connection_string may legitimately be null before the env
# step injects APPLICATIONINSIGHTS_CONNECTION_STRING; the browser smoke test
# fails hard on null where telemetry is expected.
jq -e 'has("connection_string") and has("sampling_percentage")' <<<"$telemetry" >/dev/null \
  || { echo "ERROR: /public/telemetry is missing expected fields" >&2; exit 1; }

echo "== Functional: Invoice seed + lookup =="
start_pf "svc/ic-invoice-api"
BILLER="smoke-$(date +%s)"
ACCOUNT="ACME-001"
BASE="http://127.0.0.1:${LOCAL_PORT}/billers/${BILLER}/invoices"

seeded=$($CURL -X POST "${BASE}/seed" \
  -H 'Content-Type: application/json' \
  -d "{\"count\":3,\"account_number\":\"${ACCOUNT}\"}" \
  | jq -r '.seeded')
echo "  seeded=${seeded}"
[[ "$seeded" -ge 1 ]] || { echo "ERROR: expected >=1 seeded invoice" >&2; exit 1; }

listed=$($CURL "${BASE}?account_number=${ACCOUNT}" | jq -r '.invoices | length')
echo "  listed=${listed}"
[[ "$listed" -ge 1 ]] || { echo "ERROR: expected >=1 invoice on lookup" >&2; exit 1; }

echo "== Smoke tests passed for namespace ${NAMESPACE} =="
