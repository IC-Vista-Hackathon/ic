# Contracts

REST, JSON, integer cents. Errors: `{"error": {"code", "message"}}` with conventional HTTP status.
Wire casing is **snake_case** with **lowercase string enums**, exactly as the JSON examples below
show — implemented by `Pronto.Invoice.Api`'s JSON options and by `libraries/Pronto.ServiceDefaults`
(`SnakeCaseLower` naming policy + `JsonStringEnumConverter`) for every other host. Query-string
parameters are snake_case too (`?biller_id=`, `?account_number=`).
IDs on the wire are Cosmos-generated GUID strings (`b_1a2b`, `i_77` etc. below are illustrative
shorthand, not the literal format). Per entities.md's Cosmos conventions, most containers
partition on `/biller_id` — endpoints below pass `biller_id` alongside a resource id wherever a
point read needs it, to avoid a cross-partition fan-out.

## Biller Configuration Service

| Method | Path | Purpose |
|---|---|---|
| POST | `/billers` | Start onboarding from the initial form → draft config + seeded invoices |
| GET | `/billers/{id}` | Account + current config |
| PATCH | `/billers/{id}/config` | JSON merge-patch of draft config (agents' write path) |
| POST | `/billers/{id}/config/publish` | Compliance check → new published version |
| POST | `/billers/{id}/chat` | Onboarding Agent turn: `{messages[]}` → `{reply, config}` |
| POST | `/billers/{id}/account` | Save progress: attach email/password → status `demo` |

```json
POST /billers            {"name": "City of Plano", "website": "https://plano.gov", "bill_type": "Utility"}
→ 201                    {"biller_id": "b_1a2b", "config": { ...draft config... }, "preview_url": "/preview/b_1a2b"}

PATCH /billers/b_1a2b/config
                         {"fees": {"card_percent": 2.5}, "brand": {"primary_color": "#0044cc"}}
→ 200                    {"config": { ...merged... }}
```

## Deployment Service

| Method | Path | Purpose |
|---|---|---|
| POST | `/billers/{id}/deployments` | Go live: `{isolation}` → published site |
| GET | `/billers/{id}/deployments/{deployment_id}` | Status (nested under biller — single-partition read) |

```json
POST /billers/b_1a2b/deployments   {"isolation": "shared"}
→ 201                              {"deployment_id": "d_9f", "url": "https://pay.ic.dev/plano", "status": "live"}
```

## Invoice Service

| Method | Path | Purpose |
|---|---|---|
| GET | `/billers/{id}/invoices?account_number=` | Lookup open invoices |
| GET | `/billers/{id}/invoices/{invoice_id}` | Point read (Payment Service amount lookup) |
| POST | `/billers/{id}/invoices/seed` | (Internal) seed fake data at onboarding |
| POST | `/billers/{id}/invoices/{invoice_id}/status` | (Internal) Payment Service asserts `due→paid`, `due→scheduled`, `scheduled→paid`: `{status, payment_id}` — idempotent per `payment_id`; invalid transitions 409 (`already_paid` / `invalid_transition`) |

## Payment Service

| Method | Path | Purpose |
|---|---|---|
| POST | `/payments` | Pay an invoice (mock auth) |
| GET | `/payments/{id}?biller_id=` | Receipt (single-partition read) |
| POST | `/purchases` | Biller buys the platform: `{biller_id, plan}` → Purchase status `paid`, then triggers BillerAccount status → `purchased` (see entities.md Purchase) |

```json
POST /payments           {"biller_id": "b_1a2b", "invoice_id": "i_77", "method": "card", "payer_account_id": null}
→ 201                    {"payment_id": "p_3c", "confirmation": "PRONTO-4F2A9B", "amount_cents": 8420,
                          "fee_cents": 211, "receipt_message": "Thanks from the City of Plano!"}
```

`biller_id` is required so Payment Service can write to the correct partition without first
looking up the Invoice cross-partition.

## Payer Account Service

| Method | Path | Purpose |
|---|---|---|
| POST | `/payers` | Register (offered by Policy Agent): `{biller_id, ...}` |
| GET | `/payers/{id}?biller_id=` | Profile + preferences (single-partition read) |
| PATCH | `/payers/{id}/preferences?biller_id=` | AutoPay, paperless, channels |

## Payer chat (AI Foundry)

| Method | Path | Purpose |
|---|---|---|
| POST | `/billers/{id}/payer-chat` | One turn through the payer agent pipeline: `{messages[], payer_account_id?}` → `{reply, artifacts}` |

`artifacts` carries the stage outputs so the UI can render them natively:

```json
{"bill_summary":  {"invoice_id": "i_77", "explanation": "...", "amount_cents": 8420, "due_date": "2026-07-25"},
 "payment_plan":  {"method": "ach", "when": "2026-07-24", "fee_cents": 95, "rationale": "..."},
 "needs_confirmation": true}
```

Execution only proceeds after the client posts back `{confirm: true}` for a pending plan.

## Agent tools (internal, via AI Foundry tool registry)

| Tool | Backed by | Used by |
|---|---|---|
| `update_config(patch)` | PATCH config | Onboarding, Research, Aesthetics agents; rejects patches touching `compliance` — that field is server-written only, by the publish gate |
| `research_website(url)` | crawler | Biller Research Agent |
| `run_compliance_check(config)` | rules engine | Compliance Agent; publish gate |
| `list_invoices(capability_token, account_number, include_closed)` / `get_invoice(capability_token, invoice_id)` | MCP router → Invoice Service | Bill Intelligence Agent; biller bound to the capability token, never an argument |
| `get_payment_quote(capability_token, invoice_id, method)` | MCP router → Payment Service | Bill Intelligence Agent; read-only fee quote for the payment plan, no money moves |
| `verify_payer_account(capability_token, account_number)` | MCP router → Payer Account Service | Policy Agent; account-number handshake that returns the payer-bound capability the payer-scoped tools require |
| `get_payer_profile(capability_token)` / `update_payer_preferences(capability_token, ...)` | MCP router → Payer Account Service | Policy Agent; require a payer-bound capability (update also write-capable) |
| `register_payer(capability_token, name, email, account_numbers, ...)` | MCP router → Payer Account Service | Policy Agent (payer opt-in); write-capable biller capability, biller bound to the token |
| `bind_execution_capability(capability_token)` | MCP router (server-side re-issue) | Execution Agent; at the Policy→Execution handoff, re-issues the payer-bound capability from `verify_payer_account` as an Execution-bound one (preserves biller/run/payer/write, rebinds only the agent id) so the payment tools pass the Execution-Agent-only check. No money moves |
| `create_payment_intent(capability_token, invoice_id, method, scheduled_for)` / `submit_payment(capability_token, intent_id, invoice_id, method, payer_confirmed, scheduled_for)` | MCP router → Payment Service | Execution Agent only; intent moves no money, `submit_payment` requires `payer_confirmed` post-confirmation, `intent_id` is the idempotency key; `scheduled_for` is an ISO date or explicit `null` for immediate payment. Both require a write-capable capability; an Execution-bound payer capability pays as that payer, a biller capability pays as a guest (no payer account) |
| `send_notification(biller_id, channel, template, to, payload, payer_id?)` | Notification Service | stretch |

## Notification Service (stretch)

| Method | Path | Purpose |
|---|---|---|
| POST | `/notifications` | Queue email/SMS: `{biller_id, channel, template, to, payload, payer_id?}` |
| GET | `/notifications?biller_id=&payer_id=` | History (single-partition read; `payer_id` optional — guest-pay notifications have none) |
