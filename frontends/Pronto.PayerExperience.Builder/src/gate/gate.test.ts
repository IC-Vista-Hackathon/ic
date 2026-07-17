import assert from 'node:assert/strict';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import { DeterministicSkinGenerator } from '../generators/deterministic';
import type { DesignBrief, GeneratedSkin } from '../types';
import { runContainmentGate, summarizeReport } from './index';
import { computeCoreManifest } from './manifest';

const here = dirname(fileURLToPath(import.meta.url));
const pwaDir = resolve(here, '..', '..', '..', 'Pronto.BillerPayments.Pwa');

const brief: DesignBrief = {
  biller_slug: 'demo',
  display_name: 'Demo Water',
  bill_type: 'water',
  primary_color: '#0b5d1e',
  secondary_color: '#123',
  font_family: 'Inter',
  voice_and_tone: 'calm',
  visual_style: 'modern',
  brand_keywords: ['civic'],
  assets: [],
  enabled_payment_capabilities: ['card', 'ach'],
  content: {
    heading: 'Pay your water bill',
    introduction: 'Fast and secure.',
    support_text: 'Call us.',
    privacy_policy_url: 'https://example.com/privacy',
    terms_of_service_url: 'https://example.com/terms',
  },
};

async function cleanSkin(): Promise<GeneratedSkin> {
  return new DeterministicSkinGenerator().generate(brief);
}

describe('containment gate (integration)', () => {
  it('passes a clean deterministic skin against the real pristine core', async () => {
    const expectedManifest = await computeCoreManifest(pwaDir);
    const report = await runContainmentGate({ skin: await cleanSkin(), pwaDir, expectedManifest });
    assert.equal(report.passed, true, summarizeReport(report));
    assert.deepEqual(report.violations, []);
    assert.ok(report.coreFilesVerified > 0);
    assert.deepEqual(report.checkedFiles, ['src/skin/theme.css', 'src/skin/chrome.tsx']);
  });

  it('rejects a skin that makes a network call', async () => {
    const skin = await cleanSkin();
    const expectedManifest = await computeCoreManifest(pwaDir);
    const tampered: GeneratedSkin = {
      ...skin,
      chromeTsx: `${skin.chromeTsx}\nfetch('https://evil.example.com');`,
    };
    const report = await runContainmentGate({ skin: tampered, pwaDir, expectedManifest });
    assert.equal(report.passed, false);
    assert.ok(report.violations.some(v => v.code === 'network-access'));
  });

  it('rejects a skin whose CSS reaches an undeclared origin', async () => {
    const skin = await cleanSkin();
    const expectedManifest = await computeCoreManifest(pwaDir);
    const tampered: GeneratedSkin = {
      ...skin,
      themeCss: `${skin.themeCss}\n.x{background:url(https://evil.example.com/x.png)}`,
    };
    const report = await runContainmentGate({ skin: tampered, pwaDir, expectedManifest });
    assert.equal(report.passed, false);
    assert.ok(report.violations.some(v => v.code === 'css-external-origin'));
  });

  it('rejects when the fixed core no longer matches the manifest', async () => {
    const expectedManifest = await computeCoreManifest(pwaDir);
    expectedManifest.files['src/App.tsx'] = 'deadbeef';
    const report = await runContainmentGate({ skin: await cleanSkin(), pwaDir, expectedManifest });
    assert.equal(report.passed, false);
    assert.ok(report.violations.some(v => v.code === 'core-file-mutated'));
  });

  it('the committed manifest matches the current pristine core', async () => {
    // Guards against the checked-in core-manifest.json drifting from the real PWA.
    const report = await runContainmentGate({ skin: await cleanSkin(), pwaDir });
    assert.equal(report.passed, true, summarizeReport(report));
  });
});
