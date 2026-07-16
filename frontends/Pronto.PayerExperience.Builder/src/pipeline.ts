import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { assembleBrief } from './brief';
import { buildBundle } from './build';
import { injectCspIntoBundle, runContainmentGate, summarizeReport, type GateReport } from './gate';
import { runContractGate, summarizeContractReport, type ContractGateReport } from './contract-gate';
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
  // Cross-origin surfaces the built bundle is allowed to reach. Same-origin API calls
  // (/invoices, /payments, /payers, /api) are always permitted; declare font CDNs and any
  // telemetry ingestion host here so the containment gate + CSP allow exactly those.
  containment?: {
    fontOrigins?: string[];
    cspConnectOrigins?: string[];
    cspStyleOrigins?: string[];
  };
  log?: (message: string) => void;
}

export interface PipelineResult {
  slug: string;
  revision: string;
  generator: string;
  distDir: string;
  artifactsDir: string;
  validated: boolean;
  gate: GateReport;
  csp: string;
  contractGate?: ContractGateReport;
  published?: { uploaded: number; activeBlob: string | null; sitePrefix: string };
}

// generate -> persist -> containment gate (AST + core hash manifest) -> build (typecheck
// gate) -> inject CSP -> validate (Playwright UX/a11y smoke) -> contract gate (runtime
// boundary/payment-contract conformance) -> publish. The containment gate is the safety
// precondition: it hard-fails before any build/publish work if the generated skin escapes the
// allowlist or the fixed core was tampered with. The contract gate is the correctness
// precondition for publish: it mounts the built bundle, drives it by accessibility roles, and
// asserts the requests it emits obey the payment contract.
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

  // Containment / provenance gate — runs before any build/publish work.
  log('[gate] static containment + core integrity');
  const fontOrigins = options.containment?.fontOrigins ?? [];
  const gate = await runContainmentGate({ skin, pwaDir: options.pwaDir, allowedFontOrigins: fontOrigins });
  await writeFile(join(artifactsDir, 'gate-report.json'), `${JSON.stringify(gate, null, 2)}\n`);
  log(`[gate] ${summarizeReport(gate)}`);
  if (!gate.passed) {
    throw new Error(`Containment gate rejected the bundle for ${slug}:\n${summarizeReport(gate)}`);
  }

  log('[build] typecheck + vite build');
  const { distDir } = await buildBundle({
    pwaDir: options.pwaDir,
    workRoot: options.workRoot,
    slug,
    skin,
  });

  // Runtime containment: lock the built bundle to the sanctioned surface via CSP.
  log('[gate] inject Content-Security-Policy');
  const csp = await injectCspIntoBundle(distDir, {
    connect: options.containment?.cspConnectOrigins,
    font: fontOrigins,
    style: options.containment?.cspStyleOrigins,
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

  // Runtime boundary / payment-contract conformance gate (feature F6). Runs after build,
  // before publish, alongside the F5 static containment gate. Asserts the REQUESTS the
  // generated flow emits conform to the sanctioned payment contract.
  let contractGate: ContractGateReport | undefined;
  if (options.validate !== false) {
    log('[gate] runtime payment-contract conformance');
    contractGate = await runContractGate({ distDir, slug, definitionPath: options.definitionPath });
    await writeFile(join(artifactsDir, 'contract-gate-report.json'), `${JSON.stringify(contractGate, null, 2)}\n`);
    log(`[gate] ${summarizeContractReport(contractGate)}`);
    if (!contractGate.passed) {
      throw new Error(`Contract gate rejected the bundle for ${slug}:\n${summarizeContractReport(contractGate)}`);
    }
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
    gate,
    csp,
    contractGate,
    published,
  };
}
