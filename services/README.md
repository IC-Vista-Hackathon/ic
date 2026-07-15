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
