# Services

Deterministic services own data and money movement. Service behavior follows
[`design/services.md`](../design/services.md), contracts follow
[`design/contracts.md`](../design/contracts.md), and storage follows
[`design/entities.md`](../design/entities.md).

Concrete services use the repository-wide `IC.<Capability>.<Host>` naming convention and are added
to [`IC.slnx`](../IC.slnx).

| Documented service | Concrete project | Owns |
| --- | --- | --- |
| Biller Configuration Service | `IC.BillerExperience.Api` | BillerAccount and BillerConfiguration |
| Deployment Service | `IC.BillerExperience.Worker` | Deployment publication and reconciliation |
| Invoice Service | `IC.Invoice.Api` | Invoice |
| Payment Service | `IC.Payment.Api` | Payment and Purchase |
| Payer Account Service | `IC.PayerAccount.Api` | PayerAccount |
| Notification Service | future `IC.Notification.Worker` | Notification (stretch) |

Web applications live in `frontends/`, not `services/`:

- `IC.BillerExperience.Studio` implements the Biller Onboarding Experience.
- `IC.BillerPayments.Pwa` implements the configuration-driven Payer Experience.

Agents call these deterministic service APIs through registered tools and never access service
storage directly.
