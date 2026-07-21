import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { computeCoreManifest, verifyCoreManifest, type CoreManifest } from './manifest';

let pwa: string;

async function seedPwa(dir: string): Promise<void> {
  await mkdir(join(dir, 'src', 'skin'), { recursive: true });
  await writeFile(join(dir, 'src', 'App.tsx'), 'export const App = () => null;\n');
  await writeFile(join(dir, 'src', 'provider.ts'), 'export const provider = 1;\n');
  await writeFile(join(dir, 'src', 'types.ts'), 'export type T = string;\n');
  await writeFile(join(dir, 'src', 'App.test.tsx'), 'export const t = 1;\n'); // excluded from manifest
  await writeFile(join(dir, 'src', 'skin', 'contract.ts'), 'export type C = string;\n');
  await writeFile(join(dir, 'src', 'skin', 'theme.css'), '.a{color:red}\n'); // authorable, excluded
  await writeFile(join(dir, 'src', 'skin', 'chrome.tsx'), 'export const Header = () => null;\n'); // authorable, excluded
  await writeFile(join(dir, 'index.html'), '<html></html>\n');
  await writeFile(join(dir, 'vite.config.ts'), 'export default {};\n');
}

describe('core hash manifest', () => {
  before(async () => {
    pwa = await mkdtemp(join(tmpdir(), 'gate-manifest-'));
    await seedPwa(pwa);
  });
  after(async () => {
    await rm(pwa, { recursive: true, force: true });
  });

  it('excludes authorable and test files, includes fixed core', async () => {
    const m = await computeCoreManifest(pwa);
    const names = Object.keys(m.files);
    assert.ok(names.includes('src/App.tsx'));
    assert.ok(names.includes('src/provider.ts'));
    assert.ok(names.includes('src/skin/contract.ts'));
    assert.ok(names.includes('index.html'));
    assert.ok(!names.includes('src/skin/theme.css'));
    assert.ok(!names.includes('src/skin/chrome.tsx'));
    assert.ok(!names.includes('src/App.test.tsx'));
  });

  it('verifies a pristine core with no violations', async () => {
    const expected = await computeCoreManifest(pwa);
    const { violations, verifiedCount } = await verifyCoreManifest(pwa, expected);
    assert.deepEqual(violations, []);
    assert.equal(verifiedCount, Object.keys(expected.files).length);
  });

  it('produces the same core hash for LF and CRLF checkouts', async () => {
    const expected = await computeCoreManifest(pwa);
    await writeFile(join(pwa, 'src', 'App.tsx'), 'export const App = () => null;\r\n');
    const actual = await computeCoreManifest(pwa);
    assert.equal(actual.files['src/App.tsx'], expected.files['src/App.tsx']);
    await writeFile(join(pwa, 'src', 'App.tsx'), 'export const App = () => null;\n');
  });

  it('fails when a fixed core file is mutated', async () => {
    const expected = await computeCoreManifest(pwa);
    await writeFile(join(pwa, 'src', 'App.tsx'), 'export const App = () => "tampered";\n');
    const { violations } = await verifyCoreManifest(pwa, expected);
    assert.ok(violations.some(v => v.code === 'core-file-mutated' && v.file === 'src/App.tsx'));
    await writeFile(join(pwa, 'src', 'App.tsx'), 'export const App = () => null;\n'); // restore
  });

  it('fails when the committed manifest hash is tampered with', async () => {
    const expected = await computeCoreManifest(pwa);
    const tampered: CoreManifest = { ...expected, files: { ...expected.files, 'src/App.tsx': 'deadbeef' } };
    const { violations } = await verifyCoreManifest(pwa, tampered);
    assert.ok(violations.some(v => v.code === 'core-file-mutated' && v.file === 'src/App.tsx'));
  });

  it('fails when a fixed core file is missing', async () => {
    const expected = await computeCoreManifest(pwa);
    await rm(join(pwa, 'src', 'provider.ts'));
    const { violations } = await verifyCoreManifest(pwa, expected);
    assert.ok(violations.some(v => v.code === 'core-file-missing' && v.file === 'src/provider.ts'));
    await writeFile(join(pwa, 'src', 'provider.ts'), 'export const provider = 1;\n'); // restore
  });

  it('fails when a file is added to the fixed core', async () => {
    const expected = await computeCoreManifest(pwa);
    await writeFile(join(pwa, 'src', 'sneaky.ts'), 'export const x = 1;\n');
    const { violations } = await verifyCoreManifest(pwa, expected);
    assert.ok(violations.some(v => v.code === 'core-file-added' && v.file === 'src/sneaky.ts'));
    await rm(join(pwa, 'src', 'sneaky.ts')); // restore
  });
});
