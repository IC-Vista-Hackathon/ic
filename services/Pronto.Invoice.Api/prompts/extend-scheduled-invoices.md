# Invoice Service extension prompt

Extend `Pronto.Invoice.Api` to support the flexible payer experience while remaining the invoice-state
authority. Keep `due -> scheduled`, `due -> paid`, and `scheduled -> paid` explicit and idempotent
per payment ID. Expose enough read data for typed `account-summary`, `amount-due`, and schedule
confirmation components; never return executable presentation content.

Preserve biller partitioning, DateOnly due dates, integer-cent money, snake_case contracts,
structured logs, trace correlation, and an error log on every error path. Add tests for unknown
accounts, scheduling, concurrent transitions, and public Gateway prefix rewriting.
