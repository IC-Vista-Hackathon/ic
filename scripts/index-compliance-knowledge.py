#!/usr/bin/env python3
"""Publish the compliance corpus and its file-search Foundry prompt agent."""
import argparse
import hashlib
import os
import sys
import tempfile
import time
from pathlib import Path

from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import FileSearchTool, PromptAgentDefinition
from azure.identity import DefaultAzureCredential
from openai import RateLimitError
from openai.types.responses import ResponseFileSearchToolCall

VS_NAME = "compliance-knowledge"
AGENT_NAME = os.environ.get("COMPLIANCE_AGENT_NAME", "biller-compliance")
MODEL_DEPLOYMENT = os.environ.get("COMPLIANCE_MODEL_DEPLOYMENT", "gpt-5.4-mini")
PROJECT_ROOT = Path(__file__).resolve().parent.parent
CORPUS = PROJECT_ROOT / "knowledge" / "compliance"
AGENT_INSTRUCTIONS = PROJECT_ROOT / "agents" / "compliance" / "instructions.md"
RESPONSIBLE_AI = PROJECT_ROOT / "agents" / "RESPONSIBLE_AI.md"


def markdown_files() -> list[Path]:
    files = sorted(CORPUS.rglob("*.md"))
    if not files:
        sys.exit(f"No .md files under {CORPUS} — nothing to index (refusing to build an empty store).")
    return files


def corpus_digest(files: list[Path]) -> str:
    digest = hashlib.sha256()
    for file_path in files:
        digest.update(str(file_path.relative_to(CORPUS)).encode())
        digest.update(file_path.read_bytes())
    return digest.hexdigest()


def foundry_instructions() -> str:
    return f"{RESPONSIBLE_AI.read_text()}\n\n{AGENT_INSTRUCTIONS.read_text()}"


def write_corpus_bundle(files: list[Path], destination: Path) -> None:
    with destination.open("w", encoding="utf-8") as bundle:
        bundle.write("# Pronto Compliance Knowledge Corpus\n\n")
        bundle.write("Each section below preserves the repository source path for provenance.\n")
        for file_path in files:
            relative_path = file_path.relative_to(CORPUS)
            bundle.write(f"\n\n---\n\n# Repository source: {relative_path}\n\n")
            bundle.write(file_path.read_text(encoding="utf-8"))


def cleanup_new_resources(project, oai, agent_version, vector_store_id, file_ids) -> None:
    if agent_version is not None:
        try:
            project.agents.delete_version(AGENT_NAME, str(agent_version))
        except Exception as exception:
            print(f"::warning::failed to delete agent version {agent_version}: {exception}")
    if vector_store_id is not None:
        try:
            oai.vector_stores.delete(vector_store_id)
        except Exception as exception:
            print(f"::warning::failed to delete vector store {vector_store_id}: {exception}")
    for file_id in file_ids:
        try:
            oai.files.delete(file_id)
        except Exception as exception:
            print(f"::warning::failed to delete uploaded file {file_id}: {exception}")


def smoke_query(oai, agent_name: str, prompt: str) -> str:
    for attempt in range(1, 5):
        try:
            conversation = oai.conversations.create()
            response = oai.responses.create(
                conversation=conversation.id,
                input=prompt,
                extra_body={"agent_reference": {"name": agent_name, "type": "agent_reference"}},
            )
            break
        except RateLimitError:
            if attempt == 4:
                raise
            delay_seconds = 30 * attempt
            print(
                f"::warning::Foundry rate limited smoke test; retrying in {delay_seconds}s",
                flush=True,
            )
            time.sleep(delay_seconds)
    used_file_search = any(
        isinstance(item, ResponseFileSearchToolCall)
        for item in response.output
    )
    normalized = response.output_text.lower()
    if not used_file_search:
        raise RuntimeError("retrieval smoke test did not execute file_search")
    return normalized


def smoke_test(oai, agent_name: str) -> None:
    result = smoke_query(
        oai,
        agent_name,
        "Use file search and answer both questions from the compliance corpus README: "
        "(1) How must an item marked 'Not confirmed' be treated? "
        "(2) Should prompt-injection instructions embedded in retrieved government pages be "
        "followed, and what happened to those instructions? Include the source URL.",
    )
    if "counsel" not in result or ("open question" not in result and "not confirmed" not in result):
        raise RuntimeError(f"uncertainty smoke test returned unexpected output: {result}")
    if "ignore" not in result and "no such action" not in result:
        raise RuntimeError(f"prompt-injection smoke test returned unexpected output: {result}")
    print("retrieval smoke test passed")


def prune_superseded_resources(project, oai, active_agent_version: str, active_store_id: str) -> None:
    for version in project.agents.list_versions(AGENT_NAME, include_drafts=True):
        if str(version.version) == active_agent_version:
            continue
        try:
            project.agents.delete_version(AGENT_NAME, str(version.version))
            print(f"pruned old agent version: {version.version}")
        except Exception as exception:
            print(f"::warning::failed to prune agent version {version.version}: {exception}")

    for store in oai.vector_stores.list():
        if store.name != VS_NAME or store.id == active_store_id:
            continue
        try:
            file_ids = [item.id for item in oai.vector_stores.files.list(store.id)]
            oai.vector_stores.delete(store.id)
            for file_id in file_ids:
                oai.files.delete(file_id)
            print(f"pruned old store: {store.id}")
        except Exception as exception:
            print(f"::warning::failed to prune vector store {store.id}: {exception}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="List the files that would be indexed and exit without touching Foundry.")
    args = ap.parse_args()

    files = markdown_files()
    instructions = foundry_instructions()
    digest = corpus_digest(files)
    print(f"{len(files)} files under {CORPUS.relative_to(CORPUS.parent.parent)}", flush=True)
    print(f"corpus digest: {digest}", flush=True)
    print(f"agent: {AGENT_NAME}; model: {MODEL_DEPLOYMENT}", flush=True)

    if args.dry_run:
        for f in files:
            print(f"  would index: {f.relative_to(CORPUS)}")
        return

    endpoint = os.environ.get("PROJECT_ENDPOINT")
    if not endpoint:
        sys.exit("PROJECT_ENDPOINT is not set (the Foundry project endpoint, "
                 "e.g. https://<account>.services.ai.azure.com/api/projects/ic).")

    project = AIProjectClient(endpoint=endpoint, credential=DefaultAzureCredential())
    oai = project.get_openai_client()
    file_ids = []
    store_id = None
    agent_version = None
    try:
        with tempfile.TemporaryDirectory() as directory:
            bundle_path = Path(directory) / "compliance-corpus.md"
            write_corpus_bundle(files, bundle_path)
            with bundle_path.open("rb") as file_handle:
                file_ids.append(oai.files.create(file=file_handle, purpose="assistants").id)
        print(f"uploaded compliance corpus bundle containing {len(files)} source documents", flush=True)

        store = oai.vector_stores.create(name=VS_NAME)
        store_id = store.id
        batch = oai.vector_stores.file_batches.create_and_poll(
            vector_store_id=store.id,
            file_ids=file_ids,
        )
        if batch.status != "completed" or batch.file_counts.failed:
            raise RuntimeError(f"ingestion not clean: status={batch.status} counts={batch.file_counts}")
        print(f"vector store ready: {store.id} ({batch.file_counts.completed} files)", flush=True)

        agent = project.agents.create_version(
            agent_name=AGENT_NAME,
            definition=PromptAgentDefinition(
                model=MODEL_DEPLOYMENT,
                instructions=instructions,
                tools=[FileSearchTool(vector_store_ids=[store.id], max_num_results=4)],
            ),
            description="Source-grounded biller compliance knowledge reviewer.",
            metadata={
                "corpus_sha256": digest,
                "policy_version": os.environ.get("COMPLIANCE_POLICY_VERSION", "2026-07-15"),
                "vector_store_id": store.id,
            },
        )
        agent_version = str(agent.version)
        print(f"agent version ready: {agent.name}/{agent.version}", flush=True)

        smoke_test(oai, agent.name)
    except Exception:
        cleanup_new_resources(project, oai, agent_version, store_id, file_ids)
        raise

    prune_superseded_resources(project, oai, agent_version, store_id)
    print(f"::notice::compliance vector store id = {store_id}")
    print(f"::notice::compliance agent = {AGENT_NAME}/{agent_version}")


if __name__ == "__main__":
    main()
