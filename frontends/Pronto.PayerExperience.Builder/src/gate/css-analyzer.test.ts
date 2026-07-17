import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import { analyzeSkinCss } from './css-analyzer';

const here = dirname(fileURLToPath(import.meta.url));
const pristineTheme = readFileSync(
  resolve(here, '..', '..', '..', 'Pronto.BillerPayments.Pwa', 'src', 'skin', 'theme.css'),
  'utf8',
);

const codes = (css: string, fonts: string[] = []) => analyzeSkinCss('src/skin/theme.css', css, fonts).map(v => v.code);

describe('skin CSS analyzer', () => {
  it('passes the pristine PWA theme.css', () => {
    assert.deepEqual(analyzeSkinCss('src/skin/theme.css', pristineTheme), []);
  });

  it('passes same-origin and data: resources', () => {
    assert.deepEqual(codes('.a{background:url(/pay/x/logo.svg)} .b{background:url(data:image/png;base64,AAAA)}'), []);
  });

  it('flags an external resource origin', () => {
    assert.ok(codes('.a{background:url(https://evil.example.com/x.png)}').includes('css-external-origin'));
  });

  it('allows a declared font origin', () => {
    assert.deepEqual(
      codes("@import url(https://fonts.googleapis.com/css2?family=Inter);", ['https://fonts.googleapis.com']),
      [],
    );
  });

  it('flags @import from a non-sanctioned origin', () => {
    assert.ok(codes('@import "https://evil.example.com/x.css";').includes('css-external-origin'));
  });

  it('flags javascript: URLs and expression()', () => {
    assert.ok(codes('.a{background:url(javascript:alert(1))}').includes('css-dangerous'));
    assert.ok(codes('.a{width:expression(alert(1))}').includes('css-dangerous'));
  });

  it('flags markup breakout attempts', () => {
    assert.ok(codes('.a{}</style><script>alert(1)</script>').includes('css-dangerous'));
  });

  it('ignores dangerous tokens inside comments', () => {
    assert.deepEqual(codes('/* expression( ) and url(https://x.example.com) in a comment */ .a{color:red}'), []);
  });
});
