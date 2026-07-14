# Agents

AI agents hosted in AI Foundry. One subdirectory per agent.

Agents read/write only through service APIs via registered tools — never storage directly.
See [../design/services.md](../design/services.md) for the roster and boundaries.

| Agent | Side |
|---|---|
| onboarding | Biller — chat-driven configuration orchestrator |
| biller-research | Biller — extract brand/facts from the biller's website |
| aesthetics-accessibility | Biller — review generated experience (contrast, WCAG) |
| compliance | Biller — policy check, gates publish |
| bill-intelligence | Payer — find and explain the bill |
| financial-planning | Payer — plan the payment (timing, method, fees) |
| policy | Payer — preferences + guardrails, offers account creation |
| execution | Payer — the only agent that pays, post-confirmation |
