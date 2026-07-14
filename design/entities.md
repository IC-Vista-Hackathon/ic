# Entities

All IDs are opaque strings. Money is integer cents. Timestamps ISO-8601 UTC.

## BillerAccount
The customer (org buying the platform). Created lazily — a biller can onboard and preview
anonymously; the account exists once they save progress or buy.

| Field | Type | Notes |
|---|---|---|
| id | string | |
| name | string | org name |
| website | string | used by Biller Research Agent |
| email | string | login / contact |
| status | enum | `prospect` → `demo` → `purchased` → `live` |
| tier | enum | `shared` \| `isolated` (isolation = premium) |
| created_at | timestamp | |

## BillerConfiguration
The declarative definition of a biller's payer experience. Versioned; agents edit the draft,
publish pins a version. **This is the only input to rendering a payer site.**

| Field | Type | Notes |
|---|---|---|
| biller_id | string | |
| version | int | increments on publish |
| status | enum | `draft` \| `published` |
| bill_type | string | e.g. Utility, Real Estate Tax |
| brand | object | `{primary_color, logo_text, logo_url, tagline, tone}` |
| payment_methods | string[] | `card`, `ach`, `applepay`, `googlepay`, `paypal` |
| fees | object | `{card_percent, ach_flat_cents, payer_pays_fee}` |
| features | object | `{autopay, paperless, sms_receipts, guest_pay}` |
| receipt_message | string | |
| languages | string[] | ISO codes |
| compliance | object | agent-written: accessibility + policy check results |

## Deployment
A published payer site for a biller.

| Field | Type | Notes |
|---|---|---|
| id | string | |
| biller_id | string | |
| config_version | int | pinned at publish |
| slug | string | `pay.ic.dev/{slug}` (or custom domain, isolated tier) |
| isolation | enum | `shared` \| `isolated` |
| status | enum | `preview` \| `live` \| `retired` |
| published_at | timestamp | |

## PayerAccount
Optional — guest pay needs none. Holds preferences the Policy Agent consults.

| Field | Type | Notes |
|---|---|---|
| id | string | |
| biller_id | string | scoped per biller (no cross-biller identity, v1) |
| name, email, phone | string | |
| account_numbers | string[] | linked biller account numbers |
| preferences | object | `{autopay, paperless, channels: [email, sms], payment_day}` |

## Invoice
Fake-seeded at onboarding; queried by account number.

| Field | Type | Notes |
|---|---|---|
| id | string | |
| biller_id | string | |
| account_number | string | |
| payer_name | string | |
| description | string | |
| amount_cents | int | |
| due_date | date | |
| status | enum | `due` \| `scheduled` \| `paid` |

## Payment
One attempt against an invoice (mock rails).

| Field | Type | Notes |
|---|---|---|
| id | string | |
| invoice_id | string | |
| method | string | one of the config's payment_methods |
| amount_cents | int | |
| fee_cents | int | computed from config fees |
| confirmation | string | `IC-XXXXXX` |
| status | enum | `succeeded` \| `failed` |
| created_at | timestamp | |

## Purchase
The biller buying the platform (dogfooded through our own Payment Service).

| Field | Type | Notes |
|---|---|---|
| id | string | |
| biller_id | string | |
| plan | enum | `standard` \| `isolated` |
| amount_cents | int | |
| status | enum | `pending` \| `paid` |

## Notification (stretch)

| Field | Type | Notes |
|---|---|---|
| id | string | |
| channel | enum | `email` \| `sms` |
| to | string | |
| template | string | `receipt`, `reminder`, `go_live` |
| payload | object | template variables |
| status | enum | `queued` \| `sent` |
