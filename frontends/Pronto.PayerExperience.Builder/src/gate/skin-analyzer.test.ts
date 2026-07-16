import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import { analyzeSkinTsx } from './skin-analyzer';

const here = dirname(fileURLToPath(import.meta.url));
const pristineChrome = readFileSync(
  resolve(here, '..', '..', '..', 'Pronto.BillerPayments.Pwa', 'src', 'skin', 'chrome.tsx'),
  'utf8',
);

const CLEAN = `import type { HeaderProps, IntroProps, FooterProps } from './contract';
export function Header({ brand }: HeaderProps) {
  return <header data-testid="app-header" style={{ background: brand.primary_color }} onClick={() => undefined}><strong>{brand.display_name}</strong></header>;
}
export function Intro({ eyebrow, heading, subheading }: IntroProps) {
  return <div className="intro"><span>{eyebrow}</span><h1>{heading}</h1><span>{subheading}</span></div>;
}
export function Footer({ brand, content }: FooterProps) {
  return <footer><a href={content.privacy_policy_url}>Privacy</a></footer>;
}`;

const codes = (source: string) => analyzeSkinTsx('src/skin/chrome.tsx', source).map(v => v.code);

describe('skin AST analyzer', () => {
  it('passes the pristine PWA chrome.tsx', () => {
    assert.deepEqual(analyzeSkinTsx('src/skin/chrome.tsx', pristineChrome), []);
  });

  it('passes a clean skin (React expression handlers are allowed)', () => {
    assert.deepEqual(analyzeSkinTsx('src/skin/chrome.tsx', CLEAN), []);
  });

  it('flags network access via fetch', () => {
    assert.ok(codes(`${CLEAN}\nconst x = () => fetch('/steal');`).includes('network-access'));
  });

  it('flags network access via XMLHttpRequest and WebSocket', () => {
    assert.ok(codes(`${CLEAN}\nnew XMLHttpRequest();`).includes('network-access'));
    assert.ok(codes(`${CLEAN}\nnew WebSocket('wss://x');`).includes('network-access'));
  });

  it('flags navigator.sendBeacon', () => {
    assert.ok(codes(`${CLEAN}\nnavigator.sendBeacon('/x', 'y');`).includes('network-access'));
  });

  it('flags a new npm dependency import', () => {
    assert.ok(codes(`import axios from 'axios';\n${CLEAN}`).includes('new-dependency'));
  });

  it('flags a relative import into the core', () => {
    assert.ok(codes(`import { App } from '../App';\n${CLEAN}`).includes('relative-core-import'));
  });

  it('flags a non-sanctioned sibling import', () => {
    assert.ok(codes(`import './evil';\n${CLEAN}`).includes('forbidden-import'));
  });

  it('flags dynamic import()', () => {
    assert.ok(codes(`${CLEAN}\nconst m = () => import('./contract');`).includes('dynamic-import'));
  });

  it('flags eval and Function', () => {
    assert.ok(codes(`${CLEAN}\neval('1+1');`).includes('eval-usage'));
    assert.ok(codes(`${CLEAN}\nconst f = new Function('return 1');`).includes('eval-usage'));
  });

  it('flags dangerouslySetInnerHTML', () => {
    const src = CLEAN.replace('<strong>{brand.display_name}</strong>', '<strong dangerouslySetInnerHTML={{ __html: brand.display_name }} />');
    assert.ok(codes(src).includes('dangerous-html'));
  });

  it('flags a <script> element', () => {
    const src = CLEAN.replace('<strong>{brand.display_name}</strong>', '<script>{`alert(1)`}</script>');
    assert.ok(codes(src).includes('forbidden-element'));
  });

  it('flags inline string event handlers', () => {
    const src = CLEAN.replace('onClick={() => undefined}', 'onClick="doThing()"');
    assert.ok(codes(src).includes('inline-event-handler'));
  });

  it('does not flag "fetch" used as a JSX text node or property name', () => {
    assert.deepEqual(codes(`${CLEAN}\nconst o = { fetch: 1 };\nconsole.log(o.fetch);`), []);
  });
});
