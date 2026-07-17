import { createHash } from 'node:crypto';
import { readFile, readdir, stat } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { AUTHORABLE_FILES, type GateViolation } from './contract';

// Integrity / provenance layer: a committed hash manifest of the FIXED core files (the
// payment flow in App.tsx, provider.ts, http.ts, types.ts, the skin contract, etc.). The
// gate recomputes these hashes from the pristine PWA at build time and rejects the bundle
// if any fixed file changed, was removed, or was added — so generation can never alter the
// core, and a tampered manifest is caught the same way (a legitimate core change requires
// regenerating the manifest via `npm run gate:manifest`).

export const MANIFEST_ALGORITHM = 'sha256';

export interface CoreManifest {
  algorithm: string;
  // Relative POSIX path (from the PWA root) -> hex digest.
  files: Record<string, string>;
}

const here = dirname(fileURLToPath(import.meta.url));
const COMMITTED_MANIFEST_PATH = join(here, 'core-manifest.json');

// Directories under the PWA that never ship in the served bundle and are irrelevant to
// core money/flow integrity; excluded so the manifest doesn't churn on every dev change.
const IGNORED_DIRECTORIES = new Set(['node_modules', 'dist', '.work', '.artifacts']);

// Top-level PWA entries whose contents define the served app + build. `src` carries the
// payment flow, provider, and skin contract; index.html/vite.config.ts pin the entrypoint
// and build base. Test files are excluded (they don't ship and churn constantly).
const CORE_ROOTS = ['src', 'index.html', 'vite.config.ts'];

const authorable = new Set<string>(AUTHORABLE_FILES);

function isManifestable(relPosix: string): boolean {
  if (authorable.has(relPosix)) return false;
  if (/\.test\.tsx?$/.test(relPosix)) return false;
  return true;
}

async function walk(dir: string, pwaRoot: string, out: string[]): Promise<void> {
  const entries = await readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory()) {
      if (IGNORED_DIRECTORIES.has(entry.name)) continue;
      await walk(join(dir, entry.name), pwaRoot, out);
    } else if (entry.isFile()) {
      const relPosix = relative(pwaRoot, join(dir, entry.name)).split(sep).join('/');
      if (isManifestable(relPosix)) out.push(relPosix);
    }
  }
}

function hash(body: Buffer): string {
  return createHash(MANIFEST_ALGORITHM).update(body).digest('hex');
}

// Compute the manifest of fixed core files from a pristine PWA directory.
export async function computeCoreManifest(pwaDir: string): Promise<CoreManifest> {
  const relPaths: string[] = [];
  for (const root of CORE_ROOTS) {
    const abs = join(pwaDir, root);
    if (!existsSync(abs)) continue;
    const info = await stat(abs);
    if (info.isDirectory()) {
      await walk(abs, pwaDir, relPaths);
    } else if (isManifestable(root)) {
      relPaths.push(root);
    }
  }
  relPaths.sort();
  const files: Record<string, string> = {};
  for (const rel of relPaths) {
    files[rel] = hash(await readFile(join(pwaDir, rel)));
  }
  return { algorithm: MANIFEST_ALGORITHM, files };
}

export async function loadCommittedManifest(path = COMMITTED_MANIFEST_PATH): Promise<CoreManifest> {
  return JSON.parse(await readFile(path, 'utf8')) as CoreManifest;
}

export function serializeManifest(manifest: CoreManifest): string {
  return `${JSON.stringify(manifest, null, 2)}\n`;
}

export interface VerifyManifestResult {
  violations: GateViolation[];
  verifiedCount: number;
}

// Verify the pristine PWA core against the committed manifest. Any fixed file that is
// missing, added, or whose hash differs is a violation. A tampered committed manifest
// surfaces the same way (its expected hashes no longer match the real, unchanged core).
export async function verifyCoreManifest(
  pwaDir: string,
  expected: CoreManifest,
): Promise<VerifyManifestResult> {
  const actual = await computeCoreManifest(pwaDir);
  const violations: GateViolation[] = [];
  const expectedNames = new Set(Object.keys(expected.files));
  const actualNames = new Set(Object.keys(actual.files));

  if (expected.algorithm !== actual.algorithm) {
    violations.push({
      code: 'manifest-tampered',
      file: 'core-manifest.json',
      message: `Manifest algorithm "${expected.algorithm}" does not match gate algorithm "${actual.algorithm}".`,
    });
  }

  for (const name of expectedNames) {
    if (!actualNames.has(name)) {
      violations.push({ code: 'core-file-missing', file: name, message: `Fixed core file is missing from the bundle: ${name}` });
      continue;
    }
    if (expected.files[name] !== actual.files[name]) {
      violations.push({
        code: 'core-file-mutated',
        file: name,
        message: `Fixed core file was modified (hash mismatch): ${name}. Generation must never alter core files; if this change is intentional, regenerate the manifest with \`npm run gate:manifest\`.`,
      });
    }
  }

  for (const name of actualNames) {
    if (!expectedNames.has(name)) {
      violations.push({
        code: 'core-file-added',
        file: name,
        message: `Unexpected file added to the fixed core: ${name}. Regenerate the manifest if this file is legitimately part of the core.`,
      });
    }
  }

  return { violations, verifiedCount: actualNames.size };
}
