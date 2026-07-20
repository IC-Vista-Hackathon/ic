# Agents

AI agents hosted in AI Foundry. One subdirectory per agent.

Agents read/write only through service APIs via registered tools — never storage directly.
See [../design/services.md](../design/services.md) for the roster and boundaries.

Each agent's subdirectory holds `instructions.md` (its system prompt) and `tools.json` (its desired
domain-specific allowlist, per [../design/contracts.md](../design/contracts.md)'s "Agent tools"
table). A definition is not executable until a runtime adapter implements that tool contract.
Reconciliation therefore sets `ic.enabled=true` only for the Foundry agents invoked by the current
research workflow; definition-only agents remain in inventory with `ic.enabled=false` while their
deterministic in-process counterparts run. Remote agents retain the shared `get_goal_context` and
`append_context` MCP connection for the approved tool inventory, while orchestration owns runtime
context access: it issues a
short-lived biller/run/agent capability, reads through MCP, delegates a sanitized snapshot, then
appends validated output through MCP. Capability tokens never enter model-visible prompts. Two AI
Foundry models are deployed: `gpt-5.4` for agents that plan/decide or touch
money/risk, `gpt-5.4-mini` for narrower single-purpose reads.

Agents do bounded work: perceive the supplied goal context, reason over trade-offs, invoke only
approved tools, and return typed results with provenance. `Pronto.Agentic.Orchestration` is the layer
above them: it discovers eligible agents, delegates and sequences work, supplies scoped context,
controls concurrency/timeouts, passes typed results, records activity, and decides whether the
workflow can proceed. No prompt-defined agent is the system orchestrator.

| Agent | Side | Model | Current runtime |
|---|---|---|---|
| onboarding | Biller — lead experience agent for chat-driven configuration | gpt-5.4 | deterministic (`ic.enabled=false` in Foundry) |
| research-coordinator | Biller — consolidates independently gathered cited results | gpt-5.4-mini | Foundry |
| biller-research | Biller — general public identity and payment-context research | gpt-5.4-mini | Foundry |
| biller-brand-research | Biller — first-party brand evidence specialist | gpt-5.4-mini | Foundry |
| biller-payment-policy-research | Biller — public billing and payment-policy evidence specialist | gpt-5.4-mini | Foundry |
| aesthetics-accessibility | Biller — review generated experience (contrast, WCAG) | gpt-5.4-mini | deterministic (`ic.enabled=false` in Foundry) |
| compliance | Biller — policy check, gates publish | gpt-5.4-mini | deterministic plus separately indexed Foundry evidence reviewer |
| bill-intelligence | Payer — find and explain the bill | gpt-5.4-mini | deterministic (`ic.enabled=false` in Foundry) |
| financial-planning | Payer — plan the payment (timing, method, fees) | gpt-5.4 | deterministic (`ic.enabled=false` in Foundry) |
| policy | Payer — preferences + guardrails, offers account creation | gpt-5.4 | deterministic (`ic.enabled=false` in Foundry) |
| execution | Payer — the only agent that pays, post-confirmation | gpt-5.4 | deterministic (`ic.enabled=false` in Foundry) |
