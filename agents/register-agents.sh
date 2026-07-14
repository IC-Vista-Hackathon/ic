#!/usr/bin/env bash
# Registers/updates the AI Foundry "prompt agents" for this repo's agents/<slug>/ definitions.
#
# Mechanism (researched against the live GA Foundry Agent Service REST API, api-version=v1):
#   Agents are NOT an ARM resource — `az provider show -n Microsoft.CognitiveServices` has no
#   `accounts/projects/agents` resourceType. They are project-scoped data-plane objects created
#   via POST {project-endpoint}/agents?api-version=v1, authenticated with an Entra ID bearer token
#   (DefaultAzureCredential-style — here, the `az` CLI's own token) and the "Foundry User" RBAC
#   role (Microsoft.CognitiveServices/accounts/AIServices/agents/write) at the project scope.
#
# Idempotency: GET /agents/{name} first. If it exists, POST /agents/{name} (the documented
# "update agent" op — adds a new version only if the definition actually changed; a no-op re-run
# returns the existing version). If it doesn't exist, POST /agents (create). Safe to re-run.
#
# Why bash+curl and not the .NET SDK: this repo's global.json pins .NET SDK 10.0.301, which is not
# installed in this environment (only 10.0.100 is present, and `rollForward: latestPatch` won't
# cross feature bands), so `dotnet build`/`dotnet run` fails here. `az`+`curl`+`jq` need nothing
# beyond what's already on PATH, and the whole job is nine idempotent HTTP calls.
#
# Usage:
#   az login                       # or: already-active az CLI session with project RBAC access
#   ./agents/register-agents.sh
#
# Re-run any time after editing an agent's instructions.md or tools.json; only changed agents get
# a new version.
#
# Required one-time RBAC (run once per identity that will run this script):
#   az role assignment create --assignee <principal-object-id> --role "Foundry User" \
#     --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<account>/projects/<project>

set -euo pipefail

ACCOUNT_NAME="${FOUNDRY_ACCOUNT_NAME:-aif-ic-hack-jk4zmntatjem4}"
PROJECT_NAME="${FOUNDRY_PROJECT_NAME:-ic}"
API_VERSION="v1"
ENDPOINT="https://${ACCOUNT_NAME}.cognitiveservices.azure.com/api/projects/${PROJECT_NAME}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AGENTS_DIR="$SCRIPT_DIR"

# Agent -> model deployment, per agents/README.md's table.
# (Plain case statement, not an associative array: the macOS-default /bin/bash is 3.2, which
# predates `declare -A`.)
model_for() {
  case "$1" in
    onboarding) echo "gpt-5.4" ;;
    biller-research) echo "gpt-5.4-mini" ;;
    aesthetics-accessibility) echo "gpt-5.4-mini" ;;
    compliance) echo "gpt-5.4-mini" ;;
    bill-intelligence) echo "gpt-5.4-mini" ;;
    financial-planning) echo "gpt-5.4" ;;
    policy) echo "gpt-5.4" ;;
    execution) echo "gpt-5.4" ;;
    *) echo "" ;;
  esac
}

log() { echo "[register-agents] $*" >&2; }

command -v az >/dev/null || { log "az CLI not found"; exit 1; }
command -v jq >/dev/null || { log "jq not found"; exit 1; }

TOKEN="$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)"

api() {
  # api METHOD PATH [BODY_FILE]
  local method="$1" path="$2" body_file="${3:-}"
  local args=(-sS -X "$method" "${ENDPOINT}${path}" \
    -H "Authorization: Bearer ${TOKEN}" \
    -H "Content-Type: application/json" \
    -w '\n%{http_code}')
  if [[ -n "$body_file" ]]; then
    args+=(--data-binary "@${body_file}")
  fi
  curl "${args[@]}"
}

# Convert this repo's tools.json (Chat Completions-style {"type":"function","function":{...}})
# into the flat OpenAI.Tool shape the Foundry Agents v1 API expects
# ({"type":"function","name":...,"description":...,"parameters":...}).
flatten_tools() {
  local tools_file="$1"
  jq -c '[ (.tools // [])[] | select(.type == "function") |
    {type: "function", name: .function.name, description: .function.description,
     parameters: .function.parameters} ]' "$tools_file"
}

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

exit_code=0

for dir in "$AGENTS_DIR"/*/; do
  slug="$(basename "$dir")"
  [[ -f "${dir}instructions.md" && -f "${dir}tools.json" ]] || continue

  model="$(model_for "$slug")"
  if [[ -z "$model" ]]; then
    log "SKIP ${slug}: no model mapping in this script's model_for() (check agents/README.md)"
    exit_code=1
    continue
  fi

  log "=== ${slug} (model: ${model}) ==="

  instructions="$(cat "${dir}instructions.md")"
  tools="$(flatten_tools "${dir}tools.json")"

  body_file="${TMP_DIR}/${slug}-body.json"
  jq -n --arg name "$slug" \
        --arg desc "IC agent: ${slug}" \
        --arg model "$model" \
        --arg instructions "$instructions" \
        --argjson tools "$tools" \
        '{name: $name, description: $desc,
          definition: {kind: "prompt", model: $model, instructions: $instructions, tools: $tools}}' \
    > "$body_file"

  # Does the agent already exist?
  get_resp="$(api GET "/agents/${slug}?api-version=${API_VERSION}")"
  get_code="$(tail -n1 <<<"$get_resp")"

  if [[ "$get_code" == "200" ]]; then
    log "exists -> updating (new version only if definition changed)"
    # Update body has no top-level "name".
    update_body_file="${TMP_DIR}/${slug}-update-body.json"
    jq 'del(.name)' "$body_file" > "$update_body_file"
    resp="$(api POST "/agents/${slug}?api-version=${API_VERSION}" "$update_body_file")"
  else
    log "not found (HTTP ${get_code}) -> creating"
    resp="$(api POST "/agents?api-version=${API_VERSION}" "$body_file")"
  fi

  code="$(tail -n1 <<<"$resp")"
  payload="$(sed '$d' <<<"$resp")"

  if [[ "$code" == "200" || "$code" == "201" ]]; then
    version="$(jq -r '.versions.latest.version // .version // "?"' <<<"$payload" 2>/dev/null || echo "?")"
    log "OK (HTTP ${code}), version=${version}"
  else
    log "FAILED (HTTP ${code}): ${payload}"
    exit_code=1
  fi
done

log "=== verifying: listing agents in project ${PROJECT_NAME} ==="
list_resp="$(api GET "/agents?api-version=${API_VERSION}")"
list_code="$(tail -n1 <<<"$list_resp")"
list_payload="$(sed '$d' <<<"$list_resp")"
if [[ "$list_code" == "200" ]]; then
  jq -r '.data[]?.name // .value[]?.name // empty' <<<"$list_payload"
else
  log "list FAILED (HTTP ${list_code}): ${list_payload}"
  exit_code=1
fi

exit "$exit_code"
