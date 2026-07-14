# Contracts

REST, JSON, integer cents. Errors: `{"error": {"code", "message"}}` with conventional HTTP status.

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
| GET | `/deployments/{id}` | Status |

```json
POST /billers/b_1a2b/deployments   {"isolation": "shared"}
→ 201                              {"deployment_id": "d_9f", "url": "https://pay.ic.dev/plano", "status": "live"}
```

## Invoice Service

| Method | Path | Purpose |
|---|---|---|
| GET | `/billers/{id}/invoices?account_number=` | Lookup open invoices |
| POST | `/billers/{id}/invoices/seed` | (Internal) seed fake data at onboarding |

## Payment Service

| Method | Path | Purpose |
|---|---|---|
| POST | `/payments` | Pay an invoice (mock auth) |
| GET | `/payments/{id}` | Receipt |
| POST | `/purchases` | Biller buys the platform: `{biller_id, plan}` → status `purchased` |

```json
POST /payments           {"invoice_id": "i_77", "method": "card", "payer_account_id": null}
→ 201                    {"payment_id": "p_3c", "confirmation": "IC-4F2A9B", "amount_cents": 8420,
                          "fee_cents": 211, "receipt_message": "Thanks from the City of Plano!"}
```

## Payer Account Service

| Method | Path | Purpose |
|---|---|---|
| POST | `/payers` | Register (offered by Policy Agent) |
| GET | `/payers/{id}` | Profile + preferences |
| PATCH | `/payers/{id}/preferences` | AutoPay, paperless, channels |

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
| `update_config(patch)` | PATCH config | Onboarding, Research, Aesthetics agents |
| `research_website(url)` | crawler | Biller Research Agent |
| `run_compliance_check(config)` | rules engine | Compliance Agent; publish gate |
| `get_invoices(account_number)` | Invoice Service | Bill Intelligence Agent |
| `get_preferences(payer_id)` | Payer Account Service | Policy Agent |
| `create_payer_account(...)` | Payer Account Service | Policy Agent (payer opt-in) |
| `pay_invoice(invoice_id, method)` | Payment Service | Execution Agent only, post-confirmation |
| `send_notification(channel, template, to, payload)` | Notification Service | stretch |

## Notification Service (stretch)

| Method | Path | Purpose |
|---|---|---|
| POST | `/notifications` | Queue email/SMS |
| GET | `/notifications?payer_id=` | History |
