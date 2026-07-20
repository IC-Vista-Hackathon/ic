# Foundry research adapter configuration

The adapter uses the GA Foundry Agent Service SDK (`Azure.AI.Projects` 2.0.1,
`Azure.AI.Projects.Agents` 2.0.0, and `Azure.AI.Extensions.OpenAI` 2.0.0) with Microsoft Entra
authentication. Configure:

```text
BillerExperience__Research__FoundryProjectEndpoint=https://<ai-resource>.services.ai.azure.com/api/projects/<project-name>
BillerExperience__Research__AllowedAgentIds__0=<approved-worker-agent-name>
BillerExperience__Research__CoordinatorAgentId=<optional-consolidator-agent-name>
```

Assign the API workload identity a role that permits Foundry agent data-plane operations. This
repository grants the built-in **Cognitive Services User** role at the AI Services account scope;
its data actions cover versioned-agent discovery and Responses API invocation. No API key is read by this adapter.

Production enables agent reconciliation only when the `ic-shared-context-mcp` project connection
and the `ic-agent-mcp` Kubernetes secret exist. The secret contains separate `api-key` and
`capability-signing-key` values of at least 32 characters. The API key must exactly match the
`mcpApiKey` supplied to the Bicep deployment; neither value belongs in source control.

Each worker agent must be a versioned Foundry agent and carry this metadata on its latest version:

```text
ic.approved=true
ic.capabilities=biller_research
ic.enabled=true
```

`ic.enabled` is optional and defaults to true. Metadata approval is always required. When
When `AllowedAgentIds` is non-empty it is an additional allowlist for Foundry workers; when empty, every
metadata-approved, enabled agent with the required capability is eligible. The built-in
`same-site-research` worker always remains eligible and is placed first in the worker pool. It
fetches the biller's HTML and a bounded set of same-origin HTTPS stylesheets, and its canonical
first-party brand facts take precedence over model consolidation. The coordinator agent is invoked
only through `IFoundryResearchConsolidator`; it is not included automatically in worker fan-out.

Website and stylesheet reads are bounded independently with `MaxResponseBytes`, `MaxStylesheets`,
`MaxStylesheetBytes`, and `MaxTotalStylesheetBytes`. Oversized resources retain a usable prefix and
surface a `research.response_truncated` or `research.stylesheet_truncated` warning instead of
failing the complete research run.

Web access is a property of each versioned worker agent. Provision a web-search or Bing Grounding tool (or an
approved `research_website` function backed by the hardened same-site reader) on every worker that
is expected to browse. The orchestration service does not accept uncited output: every retained
fact must contain an absolute HTTPS source.

Agents must return the cited JSON shape included in the invocation prompt. Outputs without at least one valid fact and absolute HTTPS source fail closed with `research.foundry_invalid_output`.

Application Insights exposes exclusion counts in `ic.biller.research.agent_exclusions` with a
`reason` dimension (`not_approved`, `disabled`, `not_allowlisted`, `missing_capability`, or
`agent_limit`). Response byte counts, truncations, stylesheet fetches, dispatch outcomes, and
SDK-native citation counts have separate `ic.biller.research.*` instruments.
