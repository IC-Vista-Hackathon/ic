import type { GeneratedSkin } from '../types';
import { AUTHORABLE_FILES, type GateReport, type GateViolation } from './contract';
import { analyzeSkinCss } from './css-analyzer';
import { analyzeSkinTsx } from './skin-analyzer';
import { loadCommittedManifest, verifyCoreManifest, type CoreManifest } from './manifest';

export * from './contract';
export { buildCsp, injectCspIntoBundle } from './csp';
export {
  computeCoreManifest,
  verifyCoreManifest,
  loadCommittedManifest,
  serializeManifest,
  MANIFEST_ALGORITHM,
  type CoreManifest,
} from './manifest';
export { analyzeSkinTsx } from './skin-analyzer';
export { analyzeSkinCss } from './css-analyzer';

const [THEME_FILE, CHROME_FILE] = AUTHORABLE_FILES;

export interface ContainmentGateInput {
  skin: GeneratedSkin;
  // Pristine PWA directory whose fixed core is integrity-checked against the manifest.
  pwaDir: string;
  // Font/telemetry origins the skin CSS may reference; also fed to the CSP.
  allowedFontOrigins?: string[];
  // Override the committed manifest (tests / regeneration flows).
  expectedManifest?: CoreManifest;
}

// The build-time containment / provenance gate (feature F5). Runs BEFORE build/publish:
//   1. integrity — the fixed core files match the committed hash manifest (generation
//      cannot alter App.tsx, provider.ts, types.ts, the payment flow, ...);
//   2. containment — the authorable files (theme.css, chrome.tsx) obey the allowlist:
//      no network access, only sanctioned imports, no new deps, no inline handlers /
//      dangerous HTML / <script> / eval, no external CSS origins.
// Produces a machine-readable report the pipeline logs and can attach as publish evidence.
export async function runContainmentGate(input: ContainmentGateInput): Promise<GateReport> {
  const violations: GateViolation[] = [];

  const expected = input.expectedManifest ?? (await loadCommittedManifest());
  const { violations: manifestViolations, verifiedCount } = await verifyCoreManifest(input.pwaDir, expected);
  violations.push(...manifestViolations);

  violations.push(...analyzeSkinCss(THEME_FILE, input.skin.themeCss, input.allowedFontOrigins ?? []));
  violations.push(...analyzeSkinTsx(CHROME_FILE, input.skin.chromeTsx));

  return {
    passed: violations.length === 0,
    generatedAt: new Date().toISOString(),
    checkedFiles: [THEME_FILE, CHROME_FILE],
    coreFilesVerified: verifiedCount,
    coreManifestAlgorithm: expected.algorithm,
    violations,
  };
}

// One-line human summary for pipeline logs.
export function summarizeReport(report: GateReport): string {
  if (report.passed) {
    return `containment gate PASSED (${report.coreFilesVerified} core files verified, ${report.checkedFiles.length} authorable files clean)`;
  }
  const lines = report.violations.map(v => `  - [${v.code}] ${v.file}${v.line ? `:${v.line}` : ''} ${v.message}`);
  return `containment gate FAILED with ${report.violations.length} violation(s):\n${lines.join('\n')}`;
}
