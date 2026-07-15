#!/usr/bin/env -S npx tsx
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import { runPipeline } from './pipeline';
import type { ExperienceDefinition } from './types';
import type { GeneratorMode } from './generators';

const here = dirname(fileURLToPath(import.meta.url));
const defaultPwa = resolve(here, '..', '..', 'Pronto.BillerPayments.Pwa');

interface Args {
  definition?: string;
  slug?: string;
  revision: string;
  mode: GeneratorMode;
  pwa: string;
  work: string;
  artifacts: string;
  validate: boolean;
  publish: boolean;
  writeActive: boolean;
  storage?: string;
  container?: string;
}

function parseArgs(argv: string[]): Args {
  const map = new Map<string, string>();
  const flags = new Set<string>();
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith('--')) continue;
    const key = arg.slice(2);
    const next = argv[i + 1];
    if (next && !next.startsWith('--')) {
      map.set(key, next);
      i++;
    } else {
      flags.add(key);
    }
  }
  const env = process.env;
  return {
    definition: map.get('definition') ?? env.PAYER_DEFINITION_PATH,
    slug: map.get('slug') ?? env.PAYER_SLUG,
    revision: map.get('revision') ?? env.PAYER_REVISION ?? `rev-${Date.now()}`,
    mode: (map.get('mode') ?? env.PAYER_MODE) as GeneratorMode ?? 'deterministic',
    pwa: map.get('pwa') ?? env.PAYER_PWA_DIR ?? defaultPwa,
    work: map.get('work') ?? env.PAYER_WORK ?? resolve(here, '..', '.work'),
    artifacts: map.get('artifacts') ?? env.PAYER_ARTIFACTS ?? resolve(here, '..', '.artifacts'),
    validate: !flags.has('no-validate'),
    publish: flags.has('publish') || env.PAYER_PUBLISH === 'true',
    // The Worker's config publisher owns the active.json flip; the Job only uploads the site.
    writeActive: !flags.has('no-active') && env.PAYER_SKIP_ACTIVE !== 'true',
    storage: map.get('storage') ?? env.PAYER_STORAGE_ENDPOINT,
    container: map.get('container') ?? env.PAYER_CONTAINER,
  };
}

// The definition may be provided as a file path, or (for the K8s build Job) inline as a
// base64-encoded JSON env var, which we materialize to a file so the Playwright gate can read it.
async function resolveDefinition(args: Args): Promise<{ definition: ExperienceDefinition; path: string }> {
  if (args.definition) {
    const raw = await readFile(args.definition, 'utf8');
    return { definition: JSON.parse(raw) as ExperienceDefinition, path: args.definition };
  }
  const encoded = process.env.PAYER_DEFINITION_B64;
  if (!encoded) {
    throw new Error('Provide --definition <path>, PAYER_DEFINITION_PATH, or PAYER_DEFINITION_B64.');
  }
  const raw = Buffer.from(encoded, 'base64').toString('utf8');
  await mkdir(args.work, { recursive: true });
  const path = join(args.work, 'definition.json');
  await writeFile(path, raw, 'utf8');
  return { definition: JSON.parse(raw) as ExperienceDefinition, path };
}

async function main(): Promise<void> {
  const args = parseArgs(process.argv.slice(2));
  const { definition, path: definitionPath } = await resolveDefinition(args);
  const slug = args.slug ?? definition.biller_id;

  if (args.publish && !args.storage) {
    throw new Error('--publish requires --storage <blob-endpoint> or PAYER_STORAGE_ENDPOINT.');
  }

  const result = await runPipeline({
    definition,
    definitionPath,
    slug,
    revision: args.revision,
    mode: args.mode,
    pwaDir: args.pwa,
    workRoot: args.work,
    artifactsRoot: args.artifacts,
    validate: args.validate,
    publish: args.publish && args.storage
      ? { storageEndpoint: args.storage, containerName: args.container, writeActive: args.writeActive }
      : undefined,
    log: message => console.log(message),
  });

  console.log(JSON.stringify(result, null, 2));
}

main().catch(error => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
