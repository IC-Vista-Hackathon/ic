#!/usr/bin/env -S npx tsx
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { runPipeline } from './pipeline';
import type { ExperienceDefinition } from './types';
import type { GeneratorMode } from './generators';

const here = dirname(fileURLToPath(import.meta.url));
const defaultPwa = resolve(here, '..', '..', 'Pronto.BillerPayments.Pwa');

interface Args {
  definition: string;
  slug?: string;
  revision: string;
  mode: GeneratorMode;
  pwa: string;
  work: string;
  artifacts: string;
  validate: boolean;
  publish: boolean;
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
  const definition = map.get('definition');
  if (!definition) throw new Error('--definition <path-to-experience.json> is required.');
  return {
    definition,
    slug: map.get('slug'),
    revision: map.get('revision') ?? `rev-${Date.now()}`,
    mode: (map.get('mode') as GeneratorMode) ?? 'deterministic',
    pwa: map.get('pwa') ?? defaultPwa,
    work: map.get('work') ?? resolve(here, '..', '.work'),
    artifacts: map.get('artifacts') ?? resolve(here, '..', '.artifacts'),
    validate: !flags.has('no-validate'),
    publish: flags.has('publish'),
    storage: map.get('storage') ?? process.env.PAYER_STORAGE_ENDPOINT,
    container: map.get('container'),
  };
}

async function main(): Promise<void> {
  const args = parseArgs(process.argv.slice(2));
  const definition = JSON.parse(await readFile(args.definition, 'utf8')) as ExperienceDefinition;
  const slug = args.slug ?? definition.biller_id;

  if (args.publish && !args.storage) {
    throw new Error('--publish requires --storage <blob-endpoint> or PAYER_STORAGE_ENDPOINT.');
  }

  const result = await runPipeline({
    definition,
    definitionPath: args.definition,
    slug,
    revision: args.revision,
    mode: args.mode,
    pwaDir: args.pwa,
    workRoot: args.work,
    artifactsRoot: args.artifacts,
    validate: args.validate,
    publish: args.publish && args.storage ? { storageEndpoint: args.storage, containerName: args.container } : undefined,
    log: message => console.log(message),
  });

  console.log(JSON.stringify(result, null, 2));
}

main().catch(error => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
