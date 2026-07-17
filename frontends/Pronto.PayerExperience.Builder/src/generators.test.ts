import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DeterministicSkinGenerator } from './generators/deterministic';
import type { DesignBrief } from './types';

const brief: DesignBrief = {
  biller_slug: 'city-utilities',
  display_name: 'City Utilities',
  bill_type: 'utility',
  primary_color: '#0b5',
  secondary_color: '#18e',
  font_family: 'Inter',
  voice_and_tone: 'civic',
  visual_style: 'clean',
  brand_keywords: ['civic', 'water'],
  assets: [],
  enabled_payment_capabilities: ['card', 'ach'],
  content: {
    heading: 'Pay your bill',
    introduction: 'Fast and secure.',
    support_text: 'Call us',
    privacy_policy_url: 'https://example.com/privacy',
    terms_of_service_url: 'https://example.com/terms',
  },
};

test('deterministic generator authors the F3 flow structure', async () => {
  const skin = await new DeterministicSkinGenerator().generate(brief);
  assert.ok(skin.flowTsx.length > 0, 'flow.tsx is authored');
  for (const name of ['InvoiceSelectList', 'Cart', 'BatchReview']) {
    assert.match(skin.flowTsx, new RegExp(`export function ${name}\\b`), `exports ${name}`);
  }
  for (const testId of ['invoice-select', 'cart', 'cart-total', 'batch-review', 'batch-total']) {
    assert.ok(skin.flowTsx.includes(`data-testid="${testId}"`) || skin.flowTsx.includes(testId), `renders ${testId}`);
  }
});

test('authorable flow imports only the sanctioned contract and moves no money', async () => {
  const skin = await new DeterministicSkinGenerator().generate(brief);
  // Import only from './contract'.
  const imports = [...skin.flowTsx.matchAll(/from\s+'([^']+)'/g)].map(match => match[1]);
  assert.deepEqual([...new Set(imports)], ['./contract']);
  // No network / payment / money math in the presentational flow.
  for (const forbidden of ['fetch(', 'XMLHttpRequest', 'provider', '/payments', 'amount_cents', 'FeeCalculator']) {
    assert.ok(!skin.flowTsx.includes(forbidden), `flow.tsx must not contain ${forbidden}`);
  }
});
