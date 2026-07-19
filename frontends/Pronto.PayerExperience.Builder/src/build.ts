import { execFile } from 'node:child_process';
import { cp, mkdir, rm, symlink, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { promisify } from 'node:util';
import { resolveUnderRoot, validateSlug } from './paths';
import type { GeneratedSkin } from './types';

const run = promisify(execFile);

export interface BuildInputs {
  pwaDir: string;
  workRoot: string;
  slug: string;
  skin: GeneratedSkin;
}

export interface BuildResult {
  distDir: string;
  workDir: string;
}

// Build a single biller's bundle: stage a clean copy of the PWA, overlay the generated
// skin into src/skin, then run the PWA's own typecheck + vite build with a per-biller base
// path so assets resolve under /pay/{slug}/. tsc is the first hard gate — a skin that
// breaks types or the build never reaches publish.
export async function buildBundle({ pwaDir, workRoot, slug, skin }: BuildInputs): Promise<BuildResult> {
  const pwa = resolve(pwaDir);
  const safeSlug = validateSlug(slug);
  if (!existsSync(join(pwa, 'node_modules'))) {
    throw new Error(`PWA dependencies not installed at ${pwa}/node_modules (run npm ci there first).`);
  }
  const workDir = resolveUnderRoot(workRoot, safeSlug);
  await rm(workDir, { recursive: true, force: true });
  await mkdir(workDir, { recursive: true });

  for (const entry of ['src', 'public', 'index.html', 'vite.config.ts', 'tsconfig.json', 'package.json', 'package-lock.json']) {
    const from = join(pwa, entry);
    if (existsSync(from)) await cp(from, join(workDir, entry), { recursive: true });
  }
  // Reuse the PWA's installed toolchain rather than reinstalling per build.
  await symlink(join(pwa, 'node_modules'), join(workDir, 'node_modules'), 'dir');

  await overlaySkin(workDir, skin);

  const base = `/pay/${safeSlug}/`;
  const bin = (name: string) => join(workDir, 'node_modules', '.bin', name);
  await run(bin('tsc'), ['-b'], { cwd: workDir });
  await run(bin('vite'), ['build', '--base', base], { cwd: workDir });

  return { distDir: join(workDir, 'dist'), workDir };
}

async function overlaySkin(workDir: string, skin: GeneratedSkin): Promise<void> {
  const themePath = join(workDir, 'src', 'skin', 'theme.css');
  const chromePath = join(workDir, 'src', 'skin', 'chrome.tsx');
  const flowPath = join(workDir, 'src', 'skin', 'flow.tsx');
  await mkdir(dirname(themePath), { recursive: true });
  await writeFile(themePath, skin.themeCss, 'utf8');
  await writeFile(chromePath, skin.chromeTsx, 'utf8');
  await writeFile(flowPath, skin.flowTsx, 'utf8');
}
