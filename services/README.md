# Services

Deterministic services own data and money movement. Service behavior follows
[`design/services.md`](../design/services.md), contracts follow
[`design/contracts.md`](../design/contracts.md), and storage follows
[`design/entities.md`](../design/entities.md).

Concrete services use the repository-wide `Pronto.<Capability>.<Host>` naming convention and are added
to [`Pronto.slnx`](../Pronto.slnx).

| Documented service | Concrete project | Owns |
| --- | --- | --- |
| Biller Configuration Service | `Pronto.BillerExperience.Api` | BillerAccount and BillerConfiguration |
| Deployment Service | `Pronto.BillerExperience.Worker` | Deployment publication and reconciliation |
| Invoice Service | `Pronto.Invoice.Api` | Invoice |
| Payment Service | `Pronto.Payment.Api` | Payment and Purchase |
| Payer Account Service | `Pronto.PayerAccount.Api` | PayerAccount |
| Notification Service | future `Pronto.Notification.Worker` | Notification (stretch) |

Web applications live in `frontends/`, not `services/`:

- `Pronto.BillerExperience.Studio` implements the Biller Onboarding Experience.
- `Pronto.BillerPayments.Pwa` implements the configuration-driven Payer Experience.

Agents call these deterministic service APIs through registered tools and never access service
storage directly.

## API authentication

Invoice, Payment, and Payer Account APIs validate JWT bearer tokens in Production. Biller
Experience applies the same validation to its internal purchase-transition endpoint. Production
configuration must provide:

- `Authentication__Authority`
- `Authentication__Audience` (or `Authentication__ValidAudiences__0`)
- `Authentication__ServiceScope` (the API application scope used by workload identity for
  authenticated service-to-service calls)

The API application defines the app roles in
`Pronto.ServiceDefaults.Security.ServiceClaims`. Agent identities receive only their capability
role plus a `biller_id` claim. Internal service identities receive their service role and
`service.cross-biller` where their workflow legitimately spans tenants. Development and Testing
use the explicit test authentication scheme; Production refuses to start with incomplete bearer
configuration.
