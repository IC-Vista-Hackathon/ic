// hackathon-scan-ok: execFile with an argv array, no shell — not shell-injectable
import { execFile } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { serveBundle } from './serve';

const builderDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');

export interface ValidateInputs {
  distDir: string;
  slug: string;
  definitionPath: string;
}

export interface ValidateResult {
  passed: boolean;
  output: string;
}

// Deterministic Playwright gate: serve the freshly built bundle and drive the fixed
// core flow (lookup -> method -> review -> pay) via stable data-testids the skin cannot
// touch, with all backend + config calls mocked. A generated bundle that breaks the
// happy path is rejected before it can be published.
export async function validateBundle({ distDir, slug, definitionPath }: ValidateInputs): Promise<ValidateResult> {
  const site = await serveBundle(distDir, slug);
  try {
    const result = await runPlaywright({
      PAYER_URL: site.url,
      PAYER_SLUG: slug,
      PAYER_CONFIG: resolve(definitionPath),
    });
    return result;
  } finally {
    await site.close();
  }
}

function runPlaywright(env: Record<string, string>): Promise<ValidateResult> {
  return new Promise(resolvePromise => {
    execFile(
      resolve(builderDir, 'node_modules', '.bin', 'playwright'),
      ['test', '--reporter=line'],
      { cwd: builderDir, env: { ...process.env, ...env } },
      (error, stdout, stderr) => {
        resolvePromise({ passed: !error, output: `${stdout}\n${stderr}`.trim() });
      },
    );
  });
}
