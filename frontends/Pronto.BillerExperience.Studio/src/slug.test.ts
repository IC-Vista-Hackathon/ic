import { describe, expect, it } from 'vitest';
import { toBillerSlug } from './slug';

describe('toBillerSlug', () => {
  it('keeps a clean numeric biller name without a uniqueness suffix', () => {
    expect(toBillerSlug('2222')).toBe('2222');
  });

  it('normalizes a business name into a DNS-safe slug', () => {
    expect(toBillerSlug('  City of Plano!  ')).toBe('city-of-plano');
  });

  it('makes short and empty normalized names valid', () => {
    expect(toBillerSlug('A')).toBe('biller-a');
    expect(toBillerSlug('!!!')).toBe('demo-biller');
  });

  it('keeps long slugs within the API limit without a trailing hyphen', () => {
    const slug = toBillerSlug(`${'a'.repeat(62)} hello`);

    expect(slug).toHaveLength(62);
    expect(slug).toBe('a'.repeat(62));
  });
});
