import assert from 'node:assert/strict';
import test from 'node:test';
import { toBillerSlug } from '../src/slug.ts';

test('keeps a clean numeric biller name without a uniqueness suffix', () => {
  assert.equal(toBillerSlug('2222'), '2222');
});

test('normalizes a business name into a DNS-safe slug', () => {
  assert.equal(toBillerSlug('  City of Plano!  '), 'city-of-plano');
});

test('makes short and empty normalized names valid', () => {
  assert.equal(toBillerSlug('A'), 'biller-a');
  assert.equal(toBillerSlug('!!!'), 'demo-biller');
});

test('keeps long slugs within the API limit without a trailing hyphen', () => {
  const slug = toBillerSlug(`${'a'.repeat(62)} hello`);

  assert.equal(slug.length, 62);
  assert.equal(slug, 'a'.repeat(62));
});
