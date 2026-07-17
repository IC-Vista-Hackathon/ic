// Static containment gate contract (feature F5).
//
// This module is the SINGLE SOURCE OF TRUTH for what agent-generated / authorable payer
// code is allowed to be and do. The goal is to make it *impossible by construction* for a
// generated bundle to move money or reach arbitrary endpoints: the pipeline runs the gate
// (see ./index.ts) BEFORE build/publish and hard-fails when any rule below is violated.
//
// Widening the authorable surface (siblings F3 cart, F4 installments) means editing THIS
// file — the allowlist and policy live here so there is one place to reason about the
// blast radius, and every check (AST analyzer, hash manifest, CSP) reads from here.

// Files an AI generator is permitted to author, relative to the PWA root. This must stay in
// lockstep with the PWA's own `SKIN_EDITABLE_FILES` (frontends/Pronto.BillerPayments.Pwa/
// src/skin/index.ts); `contract.test.ts` asserts the two lists are identical so the gate and
// the core never drift. When the surface widens, add the new file(s) here (and there).
export const AUTHORABLE_FILES = ['src/skin/theme.css', 'src/skin/chrome.tsx', 'src/skin/flow.tsx'] as const;
export type AuthorableFile = (typeof AUTHORABLE_FILES)[number];

// The only module specifiers a generated TypeScript/TSX file may import from. These are the
// sanctioned contract module(s): type-only surface the core exposes to the skin. Anything
// else — npm packages, or relative paths reaching into the core/payment/provider modules —
// is rejected. Specifiers are matched exactly (the skin lives beside `./contract`).
export const SANCTIONED_IMPORT_SPECIFIERS = ['./contract'] as const;

// Bare identifiers whose mere reference in generated code means a network / code-execution
// escape hatch. Matched as read references (not as a property name or a local declaration).
export const FORBIDDEN_IDENTIFIERS = [
  'fetch',
  'XMLHttpRequest',
  'WebSocket',
  'EventSource',
  'importScripts',
  'eval',
  'Function',
  'require',
] as const;

// Member accesses that are network escape hatches when reached through a global object
// (e.g. `navigator.sendBeacon`, `window.fetch`, `self.importScripts`). Matched on the
// property name, but only when the object is one of GLOBAL_OBJECTS below, so an ordinary
// `someObject.fetch` property is not a false positive.
export const FORBIDDEN_MEMBER_NAMES = ['sendBeacon', 'fetch', 'importScripts', 'open'] as const;

// Global objects through which the forbidden members above become real escape hatches.
export const GLOBAL_OBJECTS = ['window', 'globalThis', 'self', 'navigator', 'document', 'top', 'parent', 'frames'] as const;

// JSX element names that are never allowed in a generated skin.
export const FORBIDDEN_JSX_ELEMENTS = ['script'] as const;

// JSX attribute names that are never allowed in a generated skin.
export const FORBIDDEN_JSX_ATTRIBUTES = ['dangerouslySetInnerHTML'] as const;

// Runtime origins the built bundle may reach, expressed as a Content-Security-Policy. The
// sanctioned API surface (/invoices, /payments, /payers, /api) is same-origin, so `'self'`
// covers it; anything cross-origin (fonts, telemetry ingestion) must be declared explicitly.
export interface CspOrigins {
  // Extra `connect-src` origins beyond 'self' (e.g. an Application Insights ingestion host).
  connect?: string[];
  // Extra `font-src` origins beyond 'self' (declared font CDNs).
  font?: string[];
  // Extra `style-src` origins beyond 'self' (e.g. a font CSS host).
  style?: string[];
}

export type ViolationCode =
  | 'parse-error'
  | 'network-access'
  | 'forbidden-import'
  | 'new-dependency'
  | 'relative-core-import'
  | 'dynamic-import'
  | 'inline-event-handler'
  | 'dangerous-html'
  | 'forbidden-element'
  | 'eval-usage'
  | 'css-external-origin'
  | 'css-dangerous'
  | 'unexpected-authorable-file'
  | 'core-file-mutated'
  | 'core-file-missing'
  | 'core-file-added'
  | 'manifest-tampered';

export interface GateViolation {
  code: ViolationCode;
  // Authorable/core file the violation was found in, relative to the PWA root.
  file: string;
  message: string;
  // 1-based line number when the checker can localize the violation.
  line?: number;
}

export interface GateReport {
  passed: boolean;
  generatedAt: string;
  // Authorable files that were statically analyzed.
  checkedFiles: string[];
  // Number of fixed core files whose integrity was verified against the manifest.
  coreFilesVerified: number;
  coreManifestAlgorithm: string;
  violations: GateViolation[];
}
