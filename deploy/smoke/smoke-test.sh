#!/usr/bin/env bash
#
# Smoke tests for the IC service deployments.
#
# Three layers:
#   1. Deployment readiness  (kubectl) — every expected Deployment has all replicas available
#   2. Gateway reachability  (HTTP)    — each service answers through the public gateway
#   3. Functional            (HTTP)    — the Invoice seed endpoint returns a valid 201 payload
#
# Exit code 0 = all checks passed, non-zero = at least one failed (CI/cron friendly).
#
# Usage:
#   deploy/smoke/smoke-test.sh
#
# Config via env vars:
#   BASE_URL          public gateway base (default: http://ic-hack.eastus2.cloudapp.azure.com)
#   HTTP_TIMEOUT      per-request timeout seconds (default: 10)
#   SKIP_KUBECTL=1    skip layer 1 (when cluster credentials aren't available)
#
# Requires: curl, jq, and (for layer 1) kubectl with cluster credentials.

set -uo pipefail

BASE_URL="${BASE_URL:-http://ic-hack.eastus2.cloudapp.azure.com}"
HTTP_TIMEOUT="${HTTP_TIMEOUT:-10}"
SKIP_KUBECTL="${SKIP_KUBECTL:-0}"
GATEWAY_NS="kgateway-system"
GATEWAY_NAME="ic-gateway"

# Expected Deployments as "namespace/name".
EXPECTED_DEPLOYMENTS=(
  "ic/ic-biller-experience-api"
  "ic/ic-biller-experience-studio"
  "ic/ic-biller-experience-worker"
  "ic/ic-invoice-api"
  "ic/ic-payment-api"
  "ic/ic-payer-account-api"
  "biller-sites/biller-city-of-vista"
)

# Gateway reachability as "path expected_http_code". 405 = POST-only endpoint that
# still proved it is alive (the service answered, the method just isn't GET).
HTTP_CHECKS=(
  "/studio/ 200"
  "/api/ 200"
  "/invoices/ 200"
  "/pay/ 200"
  "/payments/ 405"
  "/payers/ 405"
  "/invoices/health/ready 200"
  "/api/health/ready 200"
  "/studio/health/ready 200"
  "/pay/health/ready 200"
)

PASS=0
FAIL=0
GREEN=$'\033[0;32m'; RED=$'\033[0;31m'; YELLOW=$'\033[1;33m'; DIM=$'\033[2m'; NC=$'\033[0m'

pass() { PASS=$((PASS + 1)); printf "  ${GREEN}PASS${NC}  %s\n" "$1"; }
fail() { FAIL=$((FAIL + 1)); printf "  ${RED}FAIL${NC}  %s\n" "$1"; }
section() { printf "\n${YELLOW}== %s ==${NC}\n" "$1"; }

http_code() {
  curl -s -m "$HTTP_TIMEOUT" -o /dev/null -w "%{http_code}" "$1" 2>/dev/null
}

# ---- Layer 1: Deployment readiness (kubectl) ----
section "1. Deployment readiness (kubectl)"
if [[ "$SKIP_KUBECTL" == "1" ]]; then
  printf "  ${DIM}skipped (SKIP_KUBECTL=1)${NC}\n"
elif ! command -v kubectl >/dev/null 2>&1; then
  fail "kubectl not found — cannot verify deployment readiness (set SKIP_KUBECTL=1 to skip)"
elif ! kubectl cluster-info >/dev/null 2>&1; then
  fail "no cluster access — kubectl cannot reach the cluster (set SKIP_KUBECTL=1 to skip)"
else
  for entry in "${EXPECTED_DEPLOYMENTS[@]}"; do
    ns="${entry%%/*}"; name="${entry##*/}"
    if ! kubectl get deployment "$name" -n "$ns" >/dev/null 2>&1; then
      fail "$entry — not deployed (Deployment missing)"
      continue
    fi
    desired="$(kubectl get deployment "$name" -n "$ns" -o jsonpath='{.spec.replicas}' 2>/dev/null)"
    avail="$(kubectl get deployment "$name" -n "$ns" -o jsonpath='{.status.availableReplicas}' 2>/dev/null)"
    desired="${desired:-0}"; avail="${avail:-0}"
    if [[ "$desired" -gt 0 && "$avail" -ge "$desired" ]]; then
      pass "$entry — $avail/$desired replicas available"
    else
      fail "$entry — only $avail/$desired replicas available"
    fi
  done

  gw_ip="$(kubectl get svc "$GATEWAY_NAME" -n "$GATEWAY_NS" -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null)"
  if [[ -n "$gw_ip" ]]; then
    pass "gateway $GATEWAY_NAME — public LB IP $gw_ip"
  else
    fail "gateway $GATEWAY_NAME — no LoadBalancer IP assigned"
  fi
fi

# ---- Layer 2: Gateway reachability (HTTP) ----
section "2. Gateway reachability ($BASE_URL)"
for entry in "${HTTP_CHECKS[@]}"; do
  path="${entry%% *}"; expected="${entry##* }"
  code="$(http_code "$BASE_URL$path")"
  if [[ "$code" == "$expected" ]]; then
    pass "$path -> $code"
  else
    fail "$path -> $code (expected $expected)"
  fi
done

# ---- Layer 3: Functional (Invoice seed) ----
section "3. Functional — Invoice seed"
seed_url="$BASE_URL/invoices/billers/smoke-test/invoices/seed"
resp="$(curl -s -m "$HTTP_TIMEOUT" -w $'\n%{http_code}' -X POST "$seed_url" \
  -H 'Content-Type: application/json' -d '{"count":2,"bill_type":"Utility"}' 2>/dev/null)"
code="${resp##*$'\n'}"
body="${resp%$'\n'*}"

if [[ "$code" != "201" ]]; then
  fail "POST /invoices/.../seed -> $code (expected 201)"
else
  pass "POST /invoices/.../seed -> 201"
  seeded="$(printf '%s' "$body" | jq -r '.seeded // empty' 2>/dev/null)"
  status="$(printf '%s' "$body" | jq -r '.invoices[0].status // empty' 2>/dev/null)"
  amount="$(printf '%s' "$body" | jq -r '.invoices[0].amount_cents // empty' 2>/dev/null)"

  [[ "$seeded" =~ ^[0-9]+$ && "$seeded" -ge 1 ]] \
    && pass "response: seeded=$seeded (>=1)" \
    || fail "response: seeded='$seeded' (expected integer >=1)"

  [[ "$status" == "due" ]] \
    && pass "response: invoices[0].status=due" \
    || fail "response: invoices[0].status='$status' (expected due)"

  [[ "$amount" =~ ^[0-9]+$ && "$amount" -gt 0 ]] \
    && pass "response: invoices[0].amount_cents=$amount (integer cents)" \
    || fail "response: invoices[0].amount_cents='$amount' (expected integer >0)"
fi

# ---- Summary ----
section "Summary"
printf "  %s%d passed%s, %s%d failed%s\n" "$GREEN" "$PASS" "$NC" \
  "$([[ $FAIL -gt 0 ]] && printf '%s' "$RED" || printf '%s' "$DIM")" "$FAIL" "$NC"
[[ "$FAIL" -eq 0 ]] && exit 0 || exit 1
