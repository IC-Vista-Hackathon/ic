import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { CspOrigins } from './contract';

// Runtime containment: emit a strict Content-Security-Policy into the built bundle so that,
// even if something slipped past static analysis, the bundle can only reach the sanctioned
// API surface (/invoices, /payments, /payers, /api — all same-origin, covered by 'self')
// plus explicitly declared font / telemetry origins. No arbitrary origins, no inline
// scripts, no plugins, no framing.
//
// `style-src` keeps 'unsafe-inline' because the skin sets element style attributes
// (`style={{ ... }}`) and Vite may inline critical CSS; scripts stay locked to 'self' (the
// AST gate already forbids inline/injected scripts).

function directive(name: string, values: string[]): string {
  return `${name} ${values.join(' ')}`;
}

export function buildCsp(origins: CspOrigins = {}): string {
  const connect = ["'self'", ...(origins.connect ?? [])];
  const font = ["'self'", ...(origins.font ?? [])];
  const style = ["'self'", "'unsafe-inline'", ...(origins.style ?? [])];
  return [
    directive('default-src', ["'self'"]),
    directive('base-uri', ["'self'"]),
    directive('object-src', ["'none'"]),
    directive('frame-ancestors', ["'none'"]),
    directive('form-action', ["'self'"]),
    directive('script-src', ["'self'"]),
    directive('style-src', style),
    directive('font-src', font),
    directive('img-src', ["'self'", 'data:']),
    directive('connect-src', connect),
    directive('manifest-src', ["'self'"]),
    directive('worker-src', ["'self'"]),
  ].join('; ');
}

function metaTag(policy: string): string {
  const escaped = policy.replace(/"/g, '&quot;');
  return `<meta http-equiv="Content-Security-Policy" content="${escaped}"/>`;
}

const EXISTING_CSP_META = /<meta[^>]+http-equiv=["']Content-Security-Policy["'][^>]*>/i;

// Inject (or replace) the CSP <meta> in a built dist/index.html. Returns the applied policy.
export async function injectCspIntoBundle(distDir: string, origins: CspOrigins = {}): Promise<string> {
  const policy = buildCsp(origins);
  const indexPath = join(distDir, 'index.html');
  const html = await readFile(indexPath, 'utf8');
  const tag = metaTag(policy);

  let next: string;
  if (EXISTING_CSP_META.test(html)) {
    next = html.replace(EXISTING_CSP_META, tag);
  } else if (/<head[^>]*>/i.test(html)) {
    next = html.replace(/<head[^>]*>/i, match => `${match}${tag}`);
  } else {
    throw new Error(`Cannot inject CSP: no <head> in ${indexPath}.`);
  }

  await writeFile(indexPath, next, 'utf8');
  return policy;
}
