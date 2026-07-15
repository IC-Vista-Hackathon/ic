import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { assembleBrief } from './brief';
import { buildBundle } from './build';
import { createGenerator, type GeneratorMode } from './generators';
import { publishBundle } from './publish';
import { resolveUnderRoot, validateRevision, validateSlug } from './paths';
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
  publish?: { storageEndpoint: string; containerName?: string; writeActive?: boolean };
  log?: (message: string) => void;
}

export interface PipelineResult {
  slug: string;
  revision: string;
  generator: string;
  distDir: string;
  artifactsDir: string;
  validated: boolean;
  published?: { uploaded: number; activeBlob: string | null; sitePrefix: string };
}

// generate -> persist -> build (typecheck gate) -> validate (Playwright gate) -> publish.
export async function runPipeline(options: PipelineOptions): Promise<PipelineResult> {
  const log = options.log ?? (() => {});
  const slug = validateSlug(options.slug);
  const revision = validateRevision(options.revision);
  const brief = assembleBrief(options.definition, slug, options.briefOverrides);

  const generator = createGenerator(options.mode);
  log(`[generate] ${generator.name} skin for ${slug}`);
  const skin = await generator.generate(brief);

  // Persist the brief + generated skin for provenance and revision linkage.
  const artifactsDir = resolveUnderRoot(options.artifactsRoot, slug, revision);
  await mkdir(artifactsDir, { recursive: true });
  await writeFile(join(artifactsDir, 'design-brief.json'), JSON.stringify(brief, null, 2));
  await writeFile(join(artifactsDir, 'theme.css'), skin.themeCss);
  await writeFile(join(artifactsDir, 'chrome.tsx'), skin.chromeTsx);
  if (skin.notes) await writeFile(join(artifactsDir, 'notes.txt'), skin.notes);

  log('[build] typecheck + vite build');
  const { distDir } = await buildBundle({
    pwaDir: options.pwaDir,
    workRoot: options.workRoot,
    slug,
    skin,
  });

  let validated = false;
  if (options.validate !== false) {
    log('[validate] Playwright happy-path gate');
    const result = await validateBundle({ distDir, slug, definitionPath: options.definitionPath });
    if (!result.passed) {
      throw new Error(`Validation gate failed for ${slug}:\n${result.output}`);
    }
    validated = true;
  }

  let published: PipelineResult['published'];
  if (options.publish) {
    log('[publish] uploading dist to Blob Storage');
    const result = await publishBundle({
      distDir,
      slug,
      revision,
      storageEndpoint: options.publish.storageEndpoint,
      containerName: options.publish.containerName,
      writeActive: options.publish.writeActive,
    });
    published = { uploaded: result.uploaded, activeBlob: result.activeBlob, sitePrefix: result.sitePrefix };
  }

  return {
    slug,
    revision,
    generator: generator.name,
    distDir,
    artifactsDir,
    validated,
    published,
  };
}
