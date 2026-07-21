#!/usr/bin/env bash
# Local end-to-end demo for the agent-assisted payer portal.
#
# Boots the four backend services in-memory, seeds a demo biller + invoices + payer, writes a
# local PWA config, and prints the command to start the Payer PWA. The PWA's "Payment assistant"
# panel calls the Biller Experience API's POST /billers/{billerId}/payer-chat, which runs the
# deterministic payer-side agent pipeline (Bill Intelligence -> Financial Planning) and returns a
# recommended method + timing. No Azure credentials required — everything uses the in-memory
# providers and the deterministic model provider.
#
# Usage:
#   scripts/payer-agent-demo.sh            # boot services + seed, then follow the printed steps
#   scripts/payer-agent-demo.sh --stop     # stop services started by a previous run
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PWA_DIR="$ROOT/frontends/Pronto.BillerPayments.Pwa"
RUN_DIR="${IC_DEMO_DIR:-/tmp/ic-payer-demo}"
INVOICE_PORT=5101 PAYMENT_PORT=5102 PAYER_PORT=5103 BX_PORT=5000
mkdir -p "$RUN_DIR"

stop_services() {
  if [[ -f "$RUN_DIR/pids" ]]; then
    while read -r pid; do kill "$pid" 2>/dev/null || true; done < "$RUN_DIR/pids"
    rm -f "$RUN_DIR/pids"
    echo "Stopped demo services."
  else
    echo "No running demo services recorded."
  fi
}

if [[ "${1:-}" == "--stop" ]]; then stop_services; exit 0; fi

start_service() { # name project port  extra-env...
  local name="$1" project="$2" port="$3"; shift 3
  echo "Starting $name on :$port"
  ( cd "$ROOT" && env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$port" "$@" \
      dotnet run --project "$project" ) > "$RUN_DIR/$name.log" 2>&1 &
  echo $! >> "$RUN_DIR/pids"
}

wait_ready() { # port
  local port="$1" i
  for i in $(seq 1 60); do
    if curl -sf -o /dev/null "http://localhost:$port/health/ready"; then return 0; fi
    sleep 1
  done
  echo "Service on :$port did not become ready" >&2; exit 1
}

: > "$RUN_DIR/pids"
start_service invoice services/Pronto.Invoice.Api "$INVOICE_PORT"
start_service payment services/Pronto.Payment.Api "$PAYMENT_PORT" "Services__PayerAccountApi=http://localhost:$PAYER_PORT"
start_service payer services/Pronto.PayerAccount.Api "$PAYER_PORT"
start_service billerexp services/Pronto.BillerExperience.Api "$BX_PORT" \
  "BillerExperience__SupportingServices__InvoiceBaseUrl=http://localhost:$INVOICE_PORT" \
  "BillerExperience__SupportingServices__PaymentBaseUrl=http://localhost:$PAYMENT_PORT" \
  "BillerExperience__SupportingServices__PayerAccountBaseUrl=http://localhost:$PAYER_PORT"

for p in "$INVOICE_PORT" "$PAYMENT_PORT" "$PAYER_PORT" "$BX_PORT"; do wait_ready "$p"; done
echo "All four services are ready."

# Create the demo biller (returns a full config draft we can serve to the PWA).
BID=$(curl -sf -X POST "http://localhost:$BX_PORT/billers" -H 'content-type: application/json' \
  -d '{"display_name":"Demo Water Utility","slug":"demo-water","bill_type":"utility","postal_code":"02110"}' \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['biller']['biller_id'])")
echo "Demo biller: $BID (slug demo-water)"

# Seed invoices for account 4421 and register a matching payer.
curl -sf -o /dev/null -X POST "http://localhost:$INVOICE_PORT/billers/$BID/invoices/seed" \
  -H 'content-type: application/json' -d '{"account_number":"4421","count":5,"bill_type":"utility"}'
curl -sf -o /dev/null -X POST "http://localhost:$PAYER_PORT/payers" -H 'content-type: application/json' \
  -d "{\"biller_id\":\"$BID\",\"name\":\"Alex Rivera\",\"email\":\"alex@example.com\",\"phone\":null,\"account_numbers\":[\"4421\"],\"preferences\":{\"autopay\":false,\"paperless\":false,\"channels\":[\"email\"],\"payment_day\":null}}"
echo "Seeded invoices + payer for account 4421."

# Write the local PWA config from the biller's draft definition (null optional sections stripped
# so the PWA's strict config validator accepts it).
curl -sf "http://localhost:$BX_PORT/billers/$BID/config" | python3 -c "
import json,sys
d=json.load(sys.stdin)['definition']
for k in ('billing','ui','preferences','brief'):
    if d.get(k) is None: d.pop(k, None)
json.dump(d, open('$PWA_DIR/public/config.local.json','w'), indent=2)
print('Wrote config.local.json for biller', d['biller_id'])
"

cat <<EOF

Demo data ready. Now start the PWA in a second terminal:

  cd $PWA_DIR
  VITE_CONFIG_URL=/pay/config.local.json VITE_PAYER_ASSISTANT=true npm run dev

Then open  http://localhost:5174/pay/demo-water/  and enter account number 4421.
The "Payment assistant" panel appears above the manual payment flow.

Stop the backend services with:  scripts/payer-agent-demo.sh --stop
EOF
