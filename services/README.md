# Services

Deterministic services — they own data and money movement. One subdirectory per service.

Contracts live in [../design/contracts.md](../design/contracts.md).

| Service | Owns |
|---|---|
| biller-config | BillerAccount, BillerConfiguration (versioning, publish) |
| deployment | Deployment (preview, go-live, shared vs isolated) |
| invoice | Invoice (fake seed + lookup) |
| payment | Payment, Purchase (mock rails, fees, confirmations) |
| payer-account | PayerAccount (registration, preferences, AutoPay) |
| onboarding-web | Biller onboarding webapp (form, chat, preview, dashboard) |
| payer-web | Payer portal (rendered from config; PWA) |
| notification | Email/SMS (stretch) |
