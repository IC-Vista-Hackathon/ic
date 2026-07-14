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

## Registering agents in AI Foundry

[`register-agents.sh`](./register-agents.sh) upserts each `agents/<slug>/` directory as a
"prompt agent" in the Foundry project (account `aif-ic-hack-jk4zmntatjem4`, project `ic`),
using the live GA Agent Service data-plane REST API (`{project-endpoint}/agents?api-version=v1`).

Agents are **not** an ARM resource — there is no `Microsoft.CognitiveServices/accounts/projects/agents`
resource type (confirmed via `az provider show -n Microsoft.CognitiveServices`). They're created and
versioned entirely through the project's own REST endpoint, authenticated with an Entra ID bearer
token (here, the caller's own `az` CLI token — no API keys). The equivalent SDKs are
`azure-ai-projects`/`azure-ai-agents` (Python), `@azure/ai-agents` (JS), or `Azure.AI.Agents.Persistent`
/ `Microsoft.Agents.AI.AzureAI` (.NET); the script uses `az` + `curl` + `jq` instead because this
repo's pinned .NET SDK (`global.json`, 10.0.301) isn't installed in every environment that needs to
run this, and the job is a handful of idempotent HTTP calls.

**Idempotency**: the script `GET`s each agent by name first. If it exists, it `POST`s
`/agents/{name}` — the documented "update" operation, which adds a new version only if the
definition actually changed (a no-op re-run keeps the same version number). If it doesn't exist,
it `POST`s `/agents` to create it. Re-running after editing an `instructions.md` or `tools.json`
is exactly how you roll out the change.

### Run it

```bash
az login   # if not already signed in
./agents/register-agents.sh
```

One-time prerequisite: the identity running the script needs the **Foundry User** role
(`Microsoft.CognitiveServices/accounts/AIServices/agents/write`) at the Foundry *project* scope —
Owner/Contributor at the subscription or resource-group level does **not** grant this data-plane
permission:

```bash
az role assignment create --assignee <principal-object-id> --role "Foundry User" \
  --scope /subscriptions/<sub>/resourceGroups/rg-ic-hack/providers/Microsoft.CognitiveServices/accounts/aif-ic-hack-jk4zmntatjem4/projects/ic
```

### Verify

```bash
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
curl -s "https://aif-ic-hack-jk4zmntatjem4.cognitiveservices.azure.com/api/projects/ic/agents?api-version=v1" \
  -H "Authorization: Bearer $TOKEN" | jq -r '.data[].name'
```
