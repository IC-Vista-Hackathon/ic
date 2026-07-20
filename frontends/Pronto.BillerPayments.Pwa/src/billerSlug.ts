/** The biller slug the PWA is serving, from the /pay/{slug} path with a dev-time fallback. */
export function billerSlug(): string {
  if (typeof window === 'undefined') return 'demo';
  const match = window.location.pathname.match(/^\/pay\/([^/]+)/);
  return match?.[1] ?? import.meta.env.VITE_BILLER_SLUG ?? 'demo';
}

/**
 * The preview tenant this PWA instance is targeting, from a `?preview=<preview-billerId>` query
 * param. Set by the Studio when it embeds the built bundle so the SAME shipped code renders against
 * an isolated, seeded preview tenant instead of a published biller. Absent for live payer traffic.
 */
export function previewBillerId(): string | undefined {
  if (typeof window === 'undefined') return undefined;
  const value = new URLSearchParams(window.location.search).get('preview');
  return value && value.length > 0 ? value : undefined;
}

/**
 * Where the PWA loads its experience config. In preview mode it reads the current draft config for
 * the preview tenant (biller_id already rewritten to the preview partition), so the preview reflects
 * the in-progress design; otherwise it reads the published experience for the slug.
 */
export function configEndpoint(slug: string, preview: string | undefined): string {
  return preview
    ? `/api/public/experiences/preview/${encodeURIComponent(preview)}`
    : `/api/public/experiences/${encodeURIComponent(slug)}`;
}
