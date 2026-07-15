# Foundry research adapter configuration

The adapter uses `Azure.AI.Agents.Persistent` 1.1.0 and Microsoft Entra authentication. Configure:

```text
BillerExperience__Research__FoundryProjectEndpoint=https://<ai-resource>.services.ai.azure.com/api/projects/<project-name>
BillerExperience__Research__AllowedAgentIds__0=<approved-worker-agent-id>
BillerExperience__Research__CoordinatorAgentId=<optional-consolidator-agent-id>
```

Assign the API workload identity a role that permits Foundry agent data-plane operations. This
repository grants the built-in **Cognitive Services User** role at the AI Services account scope;
its data actions cover persisted-agent discovery and invocation. No API key is read by this adapter.

Each worker agent must be a persisted Foundry agent and carry this metadata:

```text
ic.approved=true
ic.capabilities=biller_research
ic.enabled=true
```

`ic.enabled` is optional and defaults to true. Metadata approval is always required. When
`AllowedAgentIds` is non-empty it is an additional allowlist; when empty, every metadata-approved,
enabled agent with the required capability is eligible. The coordinator agent is invoked only
through `IFoundryResearchConsolidator`; it is not included automatically in worker fan-out.

Web access is a property of each persisted worker agent. Provision a Bing Grounding tool (or an
approved `research_website` function backed by the hardened same-site reader) on every worker that
is expected to browse. The orchestration service does not accept uncited output: every retained
fact must contain an absolute HTTPS source.

Agents must return the cited JSON shape included in the invocation prompt. Outputs without at least one valid fact and absolute HTTPS source fail closed with `research.foundry_invalid_output`.
