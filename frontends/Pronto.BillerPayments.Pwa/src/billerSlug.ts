/** The biller slug the PWA is serving, from the /pay/{slug} path with a dev-time fallback. */
export function billerSlug(): string {
  if (typeof window === 'undefined') return 'demo';
  const match = window.location.pathname.match(/^\/pay\/([^/]+)/);
  return match?.[1] ?? import.meta.env.VITE_BILLER_SLUG ?? 'demo';
}
