import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import { AUTHORABLE_FILES } from './contract';

const here = dirname(fileURLToPath(import.meta.url));
const skinIndex = readFileSync(
  resolve(here, '..', '..', '..', 'Pronto.BillerPayments.Pwa', 'src', 'skin', 'index.ts'),
  'utf8',
);

// The gate's authorable allowlist and the PWA's SKIN_EDITABLE_FILES must be identical — the
// gate is only sound if it knows exactly which files the core marks editable. This guards
// against the two drifting when the authorable surface widens (F3 cart, F4 installments).
describe('authorable allowlist single-source-of-truth', () => {
  it('matches the PWA SKIN_EDITABLE_FILES exactly', () => {
    const match = skinIndex.match(/SKIN_EDITABLE_FILES\s*=\s*\[([^\]]*)\]/);
    assert.ok(match, 'SKIN_EDITABLE_FILES not found in PWA skin/index.ts');
    const pwaFiles = [...match[1].matchAll(/['"]([^'"]+)['"]/g)].map(m => m[1]);
    assert.deepEqual([...AUTHORABLE_FILES].sort(), pwaFiles.sort());
  });
});
