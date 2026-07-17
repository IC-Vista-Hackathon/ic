#!/usr/bin/env -S npx tsx
import { writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { computeCoreManifest, serializeManifest } from './manifest';

// Regenerate the committed hash manifest of the PWA's fixed core files. Run this whenever a
// legitimate change lands in the payer PWA core — the containment gate fails until the
// manifest matches the pristine core again. Usage: `npm run gate:manifest [-- <pwaDir>]`.
const here = dirname(fileURLToPath(import.meta.url));
const defaultPwa = resolve(here, '..', '..', '..', 'Pronto.BillerPayments.Pwa');

async function main(): Promise<void> {
  const pwaDir = process.argv[2] ? resolve(process.argv[2]) : defaultPwa;
  const manifest = await computeCoreManifest(pwaDir);
  const out = join(here, 'core-manifest.json');
  await writeFile(out, serializeManifest(manifest));
  console.log(`Wrote ${Object.keys(manifest.files).length} core file hashes (${manifest.algorithm}) to ${out} from ${pwaDir}`);
}

main().catch(error => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
