import { mkdir, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { assembleBrief } from './brief';
import { buildBundle } from './build';
import { createGenerator, type GeneratorMode } from './generators';
import { publishBundle } from './publish';
import { validateBundle } from './validate';
import type { DesignBrief, ExperienceDefinition } from './types';

export interface PipelineOptions {
  definition: ExperienceDefinition;
  definitionPath: string;
  slug: string;
  revision: string;
  mode: GeneratorMode;
  pwaDir: string;
  workRoot: string;
  artifactsRoot: string;
  briefOverrides?: Partial<DesignBrief>;
  validate?: boolean;
  publish?: { storageEndpoint: string; containerName?: string };
  log?: (message: string) => void;
}

export interface PipelineResult {
  slug: string;
  revision: string;
  generator: string;
  distDir: string;
  artifactsDir: string;
  validated: boolean;
  published?: { uploaded: number; activeBlob: string };
}

// generate -> persist -> build (typecheck gate) -> validate (Playwright gate) -> publish.
export async function runPipeline(options: PipelineOptions): Promise<PipelineResult> {
  const log = options.log ?? (() => {});
  const brief = assembleBrief(options.definition, options.slug, options.briefOverrides);

  const generator = createGenerator(options.mode);
  log(`[generate] ${generator.name} skin for ${options.slug}`);
  const skin = await generator.generate(brief);

  // Persist the brief + generated skin for provenance and revision linkage.
  const artifactsDir = resolve(options.artifactsRoot, options.slug, options.revision);
  await mkdir(artifactsDir, { recursive: true });
  await writeFile(join(artifactsDir, 'design-brief.json'), JSON.stringify(brief, null, 2));
  await writeFile(join(artifactsDir, 'theme.css'), skin.themeCss);
  await writeFile(join(artifactsDir, 'chrome.tsx'), skin.chromeTsx);
  if (skin.notes) await writeFile(join(artifactsDir, 'notes.txt'), skin.notes);

  log('[build] typecheck + vite build');
  const { distDir } = await buildBundle({
    pwaDir: options.pwaDir,
    workRoot: options.workRoot,
    slug: options.slug,
    skin,
  });

  let validated = false;
  if (options.validate !== false) {
    log('[validate] Playwright happy-path gate');
    const result = await validateBundle({ distDir, slug: options.slug, definitionPath: options.definitionPath });
    if (!result.passed) {
      throw new Error(`Validation gate failed for ${options.slug}:\n${result.output}`);
    }
    validated = true;
  }

  let published: PipelineResult['published'];
  if (options.publish) {
    log('[publish] uploading dist to Blob Storage');
    const result = await publishBundle({
      distDir,
      slug: options.slug,
      revision: options.revision,
      storageEndpoint: options.publish.storageEndpoint,
      containerName: options.publish.containerName,
    });
    published = { uploaded: result.uploaded, activeBlob: result.activeBlob };
  }

  return {
    slug: options.slug,
    revision: options.revision,
    generator: generator.name,
    distDir,
    artifactsDir,
    validated,
    published,
  };
}
