import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { resolve } from 'node:path';
import { resolveUnderRoot, validateRevision, validateSlug } from './paths';

describe('builder paths', () => {
  it('accepts route-safe slugs and revisions', () => {
    assert.equal(validateSlug('city-of-vista'), 'city-of-vista');
    assert.equal(validateRevision('config-42'), 'config-42');
  });

  it('rejects traversal and route-breaking segments', () => {
    for (const slug of ['../escaped', 'nested/slug', '/absolute', 'Uppercase', 'slug%2fchild']) {
      assert.throws(() => validateSlug(slug));
    }
    for (const revision of ['../escaped', 'nested/revision', '/absolute', 'revision%2fchild']) {
      assert.throws(() => validateRevision(revision));
    }
  });

  it('refuses to resolve outside the supplied root', () => {
    const root = resolve('test-build-root');
    assert.equal(resolveUnderRoot(root, 'demo', 'config-1'), resolve(root, 'demo', 'config-1'));
    assert.throws(() => resolveUnderRoot(root, '..', 'outside'));
    assert.throws(() => resolveUnderRoot(root, '/outside'));
  });
});
