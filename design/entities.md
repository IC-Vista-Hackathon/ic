# Entities

Money is integer cents. Timestamps ISO-8601 UTC. Storage: Azure Cosmos DB (NoSQL API).

## Cosmos conventions

- **`id`** is a Cosmos-generated GUID string per document. No custom encoding, no sequence.
- **Partition key** is `/biller_id` almost everywhere — it's the natural tenant boundary (matches
  the existing shared-tier row-scoping rule in services.md) and keeps per-biller queries
  (the dominant access pattern) single-partition. Called out per entity below.
- **One container per entity type.** Simplest to reason about for a hackathon; no cross-container
  joins exist in Cosmos, so this only works because —
- **No foreign keys.** Cosmos doesn't enforce referential integrity and can't join across
  containers. Every `*_id` field below is a plain denormalized reference, not a FK — if a
  container is commonly looked up without its natural parent in hand, the parent's key is
  duplicated onto it (e.g. `Payment.biller_id`) so the point-read/query still hits one partition.
- Point reads (`GET /x/{id}`) need the partition key too. Where a contract endpoint only takes
  `{id}` (e.g. `GET /deployments/{id}`, `GET /payments/{id}`), that's a cross-partition fan-out
  query unless the client also passes `biller_id` — fine at hackathon scale, flagged here in case
  it matters later.

## BillerAccount
The customer (org buying the platform). Created lazily — a biller can onboard and preview
anonymously; the account exists once they save progress or buy.

Container `billers`, partition key `/id` (this is the tenant root, always looked up by its own id).

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| name | string | org name |
| website | string | used by Biller Research Agent |
| email | string | login / contact |
| status | enum | `prospect` → `demo` → `purchased` → `live` |
| tier | enum | `shared` \| `isolated` (isolation = premium) |
| created_at | timestamp | |

## BillerConfiguration
The declarative definition of a biller's payer experience. Versioned; agents edit the draft,
publish pins a version. **This is the only input to rendering a payer site.** One document per
version (draft is just the highest unpublished version) — avoids unbounded document growth.

Container `configs`, partition key `/biller_id`.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | Cosmos requires a document id; not the same as biller_id |
| biller_id | string (guid) | denormalized from BillerAccount; partition key |
| version | int | increments on publish; app must serialize writes per biller_id to keep this unique (Cosmos won't) |
| status | enum | `draft` \| `published` |
| bill_type | string | e.g. Utility, Real Estate Tax |
| brand | object | `{primary_color, logo_text, logo_url, tagline, tone}` |
| payment_methods | string[] | `card`, `ach`, `applepay`, `googlepay`, `paypal` |
| fees | object | `{card_percent, ach_flat_cents, payer_pays_fee}` |
| features | object | `{autopay, paperless, sms_receipts, guest_pay}` |
| receipt_message | string | |
| languages | string[] | ISO codes |
| compliance | object | **not agent-writable** — `run_compliance_check` output, written only by the publish endpoint server-side; `update_config` must reject patches to this field |

## Deployment
A published payer site for a biller.

Container `deployments`, partition key `/biller_id`.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| biller_id | string (guid) | denormalized from BillerAccount; partition key |
| config_version | int | pinned at publish |
| slug | string | `pay.ic.dev/{slug}` (or custom domain, isolated tier) |
| isolation | enum | `shared` \| `isolated` |
| status | enum | `live` \| `retired` |
| published_at | timestamp | |

Pre-purchase preview (README's "Preview Payer Experience") renders directly off the draft
`BillerConfiguration` — no Deployment document exists until go-live, so there's no `preview`
status to track.

## PayerAccount
Optional — guest pay needs none. Holds preferences the Policy Agent consults.

Container `payer_accounts`, partition key `/biller_id`.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| biller_id | string (guid) | partition key; scoped per biller (no cross-biller identity, v1) |
| name, email, phone | string | |
| account_numbers | string[] | linked biller account numbers (external, not a reference) |
| preferences | object | `{autopay, paperless, channels: [email, sms], payment_day}` |

## Invoice
Fake-seeded at onboarding; queried by account number.

Container `invoices`, partition key `/biller_id`.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| biller_id | string (guid) | partition key |
| account_number | string | external account identifier, not a reference |
| payer_name | string | |
| description | string | |
| amount_cents | int | |
| due_date | date | |
| status | enum | `due` \| `scheduled` \| `paid` |

## Payment
One attempt against an invoice (mock rails).

Container `payments`, partition key `/biller_id`.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| biller_id | string (guid) | denormalized from Invoice; partition key (was missing before — required since Cosmos can't join Payment → Invoice to find it) |
| invoice_id | string (guid) | denormalized reference to Invoice.id |
| payer_account_id | string (guid) \| null | denormalized reference to PayerAccount.id; contracts.md's `POST /payments` already sends this, entity was missing it |
| method | string | one of the config's payment_methods |
| amount_cents | int | |
| fee_cents | int | computed from config fees |
| confirmation | string | `IC-XXXXXX`, generated code, not a reference |
| status | enum | `scheduled` \| `succeeded` \| `failed` |
| scheduled_for | date \| null | set when Financial Planning Agent chooses "pay later"; null for immediate pay |
| created_at | timestamp | |

`Invoice.status: scheduled` mirrors a Payment in `scheduled` status against it — the scheduler
that flips a due `scheduled` Payment to `succeeded`/`failed` is a Payment Service responsibility,
not modeled as a separate entity.

## Purchase
The biller buying the platform (dogfooded through our own Payment Service).

Container `purchases`, partition key `/biller_id`.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| biller_id | string (guid) | partition key |
| plan | enum | `shared` \| `isolated` — matches `BillerAccount.tier`, not a separate vocabulary |
| amount_cents | int | |
| status | enum | `pending` \| `paid` |

When Payment Service flips a Purchase to `paid`, it must also call Biller Configuration Service to
move `BillerAccount.status` to `purchased` — a cross-service write, not automatic. Contracts.md's
`POST /purchases` description ("→ status `purchased`") refers to this BillerAccount transition,
not to Purchase's own `status` field.

## Notification (stretch)

Container `notifications`, partition key `/biller_id` — not `/payer_id`, since guest pay (no
PayerAccount) is the common receipt case and would have no payer_id to partition on.

| Field | Type | Notes |
|---|---|---|
| id | string (guid) | |
| biller_id | string (guid) | partition key |
| payer_id | string (guid) \| null | set when the recipient is a registered PayerAccount; null for guest pay |
| channel | enum | `email` \| `sms` |
| to | string | |
| template | string | `receipt`, `reminder`, `go_live` |
| payload | object | template variables |
| status | enum | `queued` \| `sent` |
