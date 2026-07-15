# Agents

AI agents hosted in AI Foundry. One subdirectory per agent.

Agents read/write only through service APIs via registered tools — never storage directly.
See [../design/services.md](../design/services.md) for the roster and boundaries.

Each agent's subdirectory holds `instructions.md` (its system prompt) and `tools.json` (its
domain-specific allowlist, per [../design/contracts.md](../design/contracts.md)'s "Agent tools"
table). Remote agents also receive the shared `get_goal_context` and `append_context` MCP tools
through their Foundry project connection; orchestration issues a short-lived biller/run/agent
capability for each invocation. Two AI Foundry models are deployed: `gpt-5.4` for agents that plan/decide or touch
money/risk, `gpt-5.4-mini` for narrower single-purpose reads.

Agents do bounded work: perceive the supplied goal context, reason over trade-offs, invoke only
approved tools, and return typed results with provenance. `IC.Agentic.Orchestration` is the layer
above them: it discovers eligible agents, delegates and sequences work, supplies scoped context,
controls concurrency/timeouts, passes typed results, records activity, and decides whether the
workflow can proceed. No prompt-defined agent is the system orchestrator.

| Agent | Side | Model |
|---|---|---|
| onboarding | Biller — lead experience agent for chat-driven configuration | gpt-5.4 |
| research-coordinator | Biller — consolidates independently gathered cited results | gpt-5.4-mini |
| biller-research | Biller — extract brand/facts from the biller's website | gpt-5.4-mini |
| aesthetics-accessibility | Biller — review generated experience (contrast, WCAG) | gpt-5.4-mini |
| compliance | Biller — policy check, gates publish | gpt-5.4-mini |
| bill-intelligence | Payer — find and explain the bill | gpt-5.4-mini |
| financial-planning | Payer — plan the payment (timing, method, fees) | gpt-5.4 |
| policy | Payer — preferences + guardrails, offers account creation | gpt-5.4 |
| execution | Payer — the only agent that pays, post-confirmation | gpt-5.4 |
