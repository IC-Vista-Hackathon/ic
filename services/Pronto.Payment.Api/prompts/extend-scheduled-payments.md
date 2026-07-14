# Payment Service extension prompt

Extend `Pronto.Payment.Api` without changing payment rails. Treat `schedule_payment` as a first-class
semantic action: require `scheduled_for`, return `scheduled`, and use the Invoice Service as the
atomic authority for `due -> scheduled`. Preserve idempotency, integer-cent money, snake_case wire
contracts, structured logs, OpenTelemetry correlation, and an error log on every error path.

Never infer immediate payment from button copy. The experience label (for example, “Pay later”)
is presentation data; `ExperienceActionType.SchedulePayment` is the authorization semantics.
Add contract and controller tests for immediate, scheduled, duplicate, invalid-date, and disabled
method paths. Do not add credentials or new money-movement integrations.
