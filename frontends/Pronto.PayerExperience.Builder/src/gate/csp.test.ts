import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, it } from 'node:test';
import { buildCsp, injectCspIntoBundle } from './csp';

describe('content-security-policy', () => {
  it('locks default/script to self and forbids objects + framing', () => {
    const csp = buildCsp();
    assert.match(csp, /default-src 'self'/);
    assert.match(csp, /script-src 'self'/);
    assert.match(csp, /object-src 'none'/);
    assert.match(csp, /frame-ancestors 'none'/);
    assert.match(csp, /connect-src 'self'/);
  });

  it('appends only declared connect/font/style origins', () => {
    const csp = buildCsp({ connect: ['https://ai.example.com'], font: ['https://fonts.example.com'] });
    assert.match(csp, /connect-src 'self' https:\/\/ai\.example\.com/);
    assert.match(csp, /font-src 'self' https:\/\/fonts\.example\.com/);
    assert.doesNotMatch(csp, /connect-src[^;]*fonts\.example\.com/);
  });

  it('injects a CSP meta tag into a built index.html', async () => {
    const dir = await mkdtemp(join(tmpdir(), 'gate-csp-'));
    try {
      await writeFile(join(dir, 'index.html'), '<!doctype html><html><head><title>x</title></head><body></body></html>');
      const policy = await injectCspIntoBundle(dir, { connect: ['https://ai.example.com'] });
      const html = await readFile(join(dir, 'index.html'), 'utf8');
      assert.match(html, /http-equiv="Content-Security-Policy"/);
      assert.ok(html.includes(policy.replace(/"/g, '&quot;')));
      assert.equal(html.match(/Content-Security-Policy/g)?.length, 1);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });

  it('replaces an existing CSP meta rather than duplicating it', async () => {
    const dir = await mkdtemp(join(tmpdir(), 'gate-csp-'));
    try {
      await writeFile(
        join(dir, 'index.html'),
        '<!doctype html><html><head><meta http-equiv="Content-Security-Policy" content="default-src *"/></head><body></body></html>',
      );
      await injectCspIntoBundle(dir);
      const html = await readFile(join(dir, 'index.html'), 'utf8');
      assert.equal(html.match(/Content-Security-Policy/g)?.length, 1);
      assert.doesNotMatch(html, /default-src \*/);
    } finally {
      await rm(dir, { recursive: true, force: true });
    }
  });
});
