import { isAbsolute, relative, resolve } from 'node:path';

const SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]{0,126}[a-z0-9])?$/;
const REVISION_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;

export function validateSlug(slug: string): string {
  if (!SLUG_PATTERN.test(slug)) {
    throw new Error('Slug must contain only lowercase letters, numbers, and interior hyphens.');
  }
  return slug;
}

export function validateRevision(revision: string): string {
  if (!REVISION_PATTERN.test(revision)) {
    throw new Error('Revision must contain only letters, numbers, dots, underscores, and hyphens.');
  }
  return revision;
}

export function resolveUnderRoot(root: string, ...segments: string[]): string {
  const resolvedRoot = resolve(root);
  const target = resolve(resolvedRoot, ...segments);
  const pathFromRoot = relative(resolvedRoot, target);
  if (!pathFromRoot || pathFromRoot.startsWith(`..${process.platform === 'win32' ? '\\' : '/'}`) || pathFromRoot === '..' || isAbsolute(pathFromRoot)) {
    throw new Error(`Resolved path must remain beneath ${resolvedRoot}.`);
  }
  return target;
}
