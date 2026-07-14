# Payment Experience Builder Reconciliation

The supplied `paymentexperiencebuilder.tar.gz` is the interaction and visual reference for the
biller Studio and payer experience. The IC repository remains the source of truth for runtime
architecture, contracts, orchestration, persistence, service integrations, observability, and
deployment.

## Adopted experience model

- Guest checkout
- AutoPay availability and enrollment during checkout
- Paperless enrollment
- Reminder channel
- Selected payment methods, constrained by existing payment rails
- Account history and account update self-service
- Fee handling
- Desktop and mobile preview modes
- Payment, history, communication, and complex preview scenarios
- Recommendation rationale

These values are stored in `BillerExperienceDefinition.Preferences`. Existing definitions without
preferences are rendered with safe defaults, so published artifacts remain backward compatible.

## Preserved IC architecture

- Chat is the mutation interface for biller configuration.
- The Biller Experience API owns versioned definitions and approval state.
- Agent activity is streamed from observable orchestration events.
- Payment capabilities describe existing rails and are never invented by the UI or model.
- The shared payer PWA renders every biller from storage-backed configuration.
- Invoice, Payment, and Payer Account APIs remain the systems used by the payer flow.
- `SchedulePayment` remains distinct from immediate payment even when its label is customized.

## Design language

The implementation follows the supplied reference's InvoiceCloud visual conventions: token-like
spacing, 10–14 pixel radii, restrained surfaces, Inter-style typography, monospaced monetary values,
clear focus rings, verb-first action labels, desktop/mobile previews, scenario tabs, and structured
recommendation cards. Prototype-only uploads, embedded sample documents, and browser-only business
state are intentionally excluded.
