# Agents

AI agents hosted in AI Foundry. One subdirectory per agent.

Agents read/write only through service APIs via registered tools — never storage directly.
See [../design/services.md](../design/services.md) for the roster and boundaries.

Each agent's subdirectory holds `instructions.md` (its system prompt) and `tools.json` (its
allowed tool definitions, per [../design/contracts.md](../design/contracts.md)'s "Agent tools"
table). Two AI Foundry models are deployed: `gpt-5.4` for agents that plan/decide or touch
money/risk, `gpt-5.4-mini` for narrower single-purpose reads.

| Agent | Side | Model |
|---|---|---|
| onboarding | Biller — chat-driven configuration orchestrator | gpt-5.4 |
| biller-research | Biller — extract brand/facts from the biller's website | gpt-5.4-mini |
| aesthetics-accessibility | Biller — review generated experience (contrast, WCAG) | gpt-5.4-mini |
| compliance | Biller — policy check, gates publish | gpt-5.4-mini |
| bill-intelligence | Payer — find and explain the bill | gpt-5.4-mini |
| financial-planning | Payer — plan the payment (timing, method, fees) | gpt-5.4 |
| policy | Payer — preferences + guardrails, offers account creation | gpt-5.4 |
| execution | Payer — the only agent that pays, post-confirmation | gpt-5.4 |
