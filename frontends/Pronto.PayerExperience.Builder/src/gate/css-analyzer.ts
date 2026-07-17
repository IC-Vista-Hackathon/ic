import type { GateViolation } from './contract';

// Containment checks for a generated stylesheet. CSS is data, not executable code, so the
// relevant attack surface is narrow and lexical: external resource origins (`url(...)`,
// `@import`) that aren't declared font hosts, `javascript:`/`expression()` execution
// vectors, and attempts to break out of the <style> context. We tokenize comment-stripped
// CSS rather than matching raw regex against the whole file so string coincidences in, say,
// declared content don't cause false hits.

function stripComments(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, ' ');
}

function lineOf(source: string, index: number): number {
  return source.slice(0, index).split('\n').length;
}

function originAllowed(rawUrl: string, allowedFontOrigins: string[]): boolean {
  const url = rawUrl.trim().replace(/^['"]|['"]$/g, '');
  // Same-origin relative refs and inline data URIs are fine; only external origins matter.
  if (url.startsWith('data:')) return true;
  if (!/^(https?:)?\/\//i.test(url)) return true;
  return allowedFontOrigins.some(origin => {
    const normalized = origin.replace(/^https?:/i, '').replace(/\/$/, '');
    const target = url.replace(/^https?:/i, '');
    return target === normalized || target.startsWith(`${normalized}/`);
  });
}

export function analyzeSkinCss(
  fileRel: string,
  source: string,
  allowedFontOrigins: string[] = [],
): GateViolation[] {
  const violations: GateViolation[] = [];
  const css = stripComments(source);
  const add = (code: GateViolation['code'], index: number, message: string) =>
    violations.push({ code, file: fileRel, message, line: lineOf(source, index) });

  // Execution / breakout vectors.
  for (const match of css.matchAll(/javascript\s*:/gi)) {
    add('css-dangerous', match.index ?? 0, 'javascript: URLs are forbidden in generated CSS.');
  }
  for (const match of css.matchAll(/expression\s*\(/gi)) {
    add('css-dangerous', match.index ?? 0, 'CSS expression() is forbidden in generated CSS.');
  }
  for (const match of css.matchAll(/<\s*\/?\s*(script|style)/gi)) {
    add('css-dangerous', match.index ?? 0, 'Markup tags are forbidden inside generated CSS.');
  }

  // External resource origins via url(...) and @import.
  for (const match of css.matchAll(/url\(\s*([^)]+?)\s*\)/gi)) {
    const url = match[1];
    if (!originAllowed(url, allowedFontOrigins)) {
      add('css-external-origin', match.index ?? 0, `External resource origin not allowed: url(${url.trim()}). Declare font origins to permit them.`);
    }
  }
  for (const match of css.matchAll(/@import\s+(?:url\(\s*)?([^;)\s]+)/gi)) {
    const url = match[1];
    if (!originAllowed(url, allowedFontOrigins)) {
      add('css-external-origin', match.index ?? 0, `@import from a non-sanctioned origin is forbidden: ${url.trim()}.`);
    }
  }

  return violations;
}
