# Payer Account Service extension prompt

Extend `Pronto.PayerAccount.Api` for payer experiences that optionally collect AutoPay, paperless, and
notification preferences. Registration must remain explicit: never create a payer merely because
an agent changed UI copy. A scheduled one-time payment does not imply AutoPay. Keep payment-day
validation, separate opt-ins, biller scoping, and idempotent account linking.

Use the existing typed contracts and snake_case wire format. Add structured logs and OpenTelemetry
for registration/preference paths, log every error path, and test missing consent data, duplicate
registration, preference updates, and schedule-payment flows with no AutoPay enrollment.
