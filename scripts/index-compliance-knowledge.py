#!/usr/bin/env python3
"""Populate the production AI Foundry file-search index from knowledge/compliance/.

Basic Foundry setup (Microsoft-managed store): we upload the markdown files and build a
vector store the compliance agent can file-search. Runs from CI on merge to main — see
.github/workflows/index-compliance-knowledge.yml.

Idempotent: each run creates a fresh vector store named VS_NAME, then prunes older stores
of the same name so repeated merges don't pile up.

Auth: DefaultAzureCredential (the azure/login service principal in CI, or your `az login`
locally). azure-ai-projects' get_openai_client() resolves data-plane access from there.

Verified against azure-ai-projects 2.3.0 + Foundry project `ic` on 2026-07-14: upload →
vector store → batch ingest → prune all succeed end-to-end.
"""
import argparse
import os
import sys
from pathlib import Path

VS_NAME = "compliance-knowledge"
CORPUS = Path(__file__).resolve().parent.parent / "knowledge" / "compliance"


def markdown_files() -> list[Path]:
    files = sorted(CORPUS.rglob("*.md"))
    if not files:
        sys.exit(f"No .md files under {CORPUS} — nothing to index (refusing to build an empty store).")
    return files


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true",
                    help="List the files that would be indexed and exit without touching Foundry.")
    args = ap.parse_args()

    files = markdown_files()
    print(f"{len(files)} files under {CORPUS.relative_to(CORPUS.parent.parent)}")

    if args.dry_run:
        for f in files:
            print(f"  would index: {f.relative_to(CORPUS)}")
        return

    # Imported lazily so --dry-run works without the SDK installed.
    from azure.ai.projects import AIProjectClient
    from azure.identity import DefaultAzureCredential

    endpoint = os.environ.get("PROJECT_ENDPOINT")
    if not endpoint:
        sys.exit("PROJECT_ENDPOINT is not set (the Foundry project endpoint, "
                 "e.g. https://<account>.services.ai.azure.com/api/projects/ic).")

    # azure-ai-projects 2.x exposes the agents data plane through an OpenAI-compatible client;
    # files + vector stores use the standard OpenAI surface from here.
    oai = AIProjectClient(endpoint=endpoint, credential=DefaultAzureCredential()).get_openai_client()

    file_ids = []
    for f in files:
        with open(f, "rb") as fh:
            file_ids.append(oai.files.create(file=fh, purpose="assistants").id)
    print(f"uploaded {len(file_ids)} files")

    store = oai.vector_stores.create(name=VS_NAME)
    # create_and_poll blocks until parse/chunk/embed finishes, so a green run means the index
    # is actually queryable — not merely that the upload was accepted.
    batch = oai.vector_stores.file_batches.create_and_poll(vector_store_id=store.id, file_ids=file_ids)
    if batch.status != "completed" or batch.file_counts.failed:
        sys.exit(f"ingestion not clean: status={batch.status} counts={batch.file_counts}")
    print(f"vector store ready: {store.id} ({batch.file_counts.completed} files)")

    # Prune superseded stores of the same name so repeated merges don't accumulate. Only touches
    # stores this workflow created (matched by name); leaves everything else alone.
    for vs in oai.vector_stores.list():
        if vs.name == VS_NAME and vs.id != store.id:
            oai.vector_stores.delete(vs.id)
            print(f"pruned old store: {vs.id}")

    # NOTE: attaching this store to the compliance agent is a separate step — the agent's
    # file_search tool needs store.id in its tool_resources. Not automated here (the agent has
    # no file_search wiring yet). Print the id so the attach step / operator can pick it up.
    print(f"::notice::compliance vector store id = {store.id}")


if __name__ == "__main__":
    main()
