export function toBillerSlug(displayName: string): string {
  const normalized = displayName
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 63)
    .replace(/-+$/g, '');

  if (normalized.length >= 3) return normalized;
  if (normalized.length > 0) return `biller-${normalized}`;
  return 'demo-biller';
}
