import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from './api';
import { trackEvent } from './insights';
import type { PreviewTenant } from './types';

type PreviewStatus = 'unavailable' | 'provisioning' | 'ready' | 'resetting' | 'error';

/**
 * Embeds the SAME built payer PWA that ships, pointed at an isolated, seeded preview tenant — so the
 * Studio preview is the real code path against real services (fake rails), not client-side sample
 * data. Owns the preview tenant lifecycle: provision on open, and a "Restart preview" that wipes +
 * deterministically re-seeds the tenant before reloading the embedded bundle.
 */
export function PreviewPane({
  billerId,
  device,
  previewBaseUrl = import.meta.env.VITE_PWA_PREVIEW_URL ?? '/pay/',
}: {
  billerId: string | null;
  device: 'desktop' | 'mobile';
  previewBaseUrl?: string;
}) {
  const [status, setStatus] = useState<PreviewStatus>('provisioning');
  const [tenant, setTenant] = useState<PreviewTenant>();
  const [error, setError] = useState('');
  // Bumped on every (re)seed so the iframe remounts and reloads the freshly seeded tenant.
  const [nonce, setNonce] = useState(0);
  const requestId = useRef(0);

  const provision = useCallback(async () => {
    if (!billerId) { setStatus('unavailable'); return; }
    const attempt = ++requestId.current;
    setStatus('provisioning'); setError('');
    try {
      const descriptor = await api.provisionPreview(billerId);
      if (attempt !== requestId.current) return; // a newer provision/reset superseded this one
      setTenant(descriptor);
      setNonce(value => value + 1);
      setStatus('ready');
      trackEvent('studio.preview_provisioned', { biller_id: billerId });
    } catch (caught) {
      if (attempt !== requestId.current) return;
      setError(caught instanceof Error ? caught.message : 'Could not prepare the preview.');
      setStatus('error');
    }
  }, [billerId]);

  useEffect(() => { void provision(); }, [provision]);

  const reset = useCallback(async () => {
    if (!billerId) return;
    const attempt = ++requestId.current;
    setStatus('resetting'); setError('');
    try {
      const descriptor = await api.resetPreview(billerId);
      if (attempt !== requestId.current) return;
      setTenant(descriptor);
      setNonce(value => value + 1);
      setStatus('ready');
      trackEvent('studio.preview_reset', { biller_id: billerId });
    } catch (caught) {
      if (attempt !== requestId.current) return;
      setError(caught instanceof Error ? caught.message : 'Could not reset the preview.');
      setStatus('error');
    }
  }, [billerId]);

  if (status === 'unavailable') {
    return (
      <div data-testid="preview-unavailable" role="status" style={note}>
        Save this experience first to preview it against live services.
      </div>
    );
  }

  if (status === 'error') {
    return (
      <div data-testid="preview-error" role="alert" style={{ ...note, background: '#fff1f0', borderColor: '#e8b0aa' }}>
        <div style={{ marginBottom: 10 }}>{error}</div>
        <button type="button" onClick={() => void provision()} style={buttonPrimary}>Try again</button>
      </div>
    );
  }

  const width = device === 'mobile' ? 390 : 1000;
  const iframeSrc = tenant
    ? `${previewBaseUrl}?preview=${encodeURIComponent(tenant.preview_biller_id)}&_r=${nonce}`
    : undefined;

  return (
    <div style={{ width: '100%', maxWidth: width, margin: '0 auto' }}>
      <div style={toolbar}>
        <span data-testid="preview-badge" style={badge}>Preview tenant · synthetic data · fake rails</span>
        <button
          type="button"
          data-testid="preview-reset"
          onClick={() => void reset()}
          disabled={status === 'resetting'}
          style={buttonSecondary}>
          {status === 'resetting' ? 'Resetting…' : 'Restart preview'}
        </button>
      </div>
      <div style={{ position: 'relative', borderRadius: 14, overflow: 'hidden', boxShadow: 'var(--invoicecloud-elevation-3)', background: '#fff' }}>
        {status === 'provisioning' && (
          <div data-testid="preview-provisioning" role="status" style={overlay}>Seeding a fresh preview tenant…</div>
        )}
        {status === 'resetting' && (
          <div data-testid="preview-resetting" role="status" style={overlay}>Wiping and re-seeding…</div>
        )}
        {iframeSrc && (
          <iframe
            key={nonce}
            data-testid="preview-frame"
            title="Payer experience preview"
            src={iframeSrc}
            style={{ width: '100%', height: 720, border: '0', display: 'block' }} />
        )}
      </div>
    </div>
  );
}

const note: React.CSSProperties = {
  width: '100%', maxWidth: 1000, margin: '0 auto', padding: 20, borderRadius: 14,
  border: '1px solid var(--invoicecloud-surface-default-border)', background: '#fff',
  fontSize: 14, textAlign: 'center',
};
const toolbar: React.CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
  gap: 12, marginBottom: 10,
};
const badge: React.CSSProperties = {
  fontSize: 12, fontWeight: 700, padding: '6px 12px', borderRadius: 999,
  background: '#f6e6c8', color: '#7a4b00',
};
const overlay: React.CSSProperties = {
  position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
  background: 'rgba(255,255,255,.85)', fontSize: 14, fontWeight: 600, zIndex: 1,
};
const buttonPrimary: React.CSSProperties = {
  background: 'var(--invoicecloud-primary)', color: '#fff', border: 'none',
  borderRadius: 8, padding: '10px 18px', fontSize: 14, fontWeight: 700, cursor: 'pointer',
};
const buttonSecondary: React.CSSProperties = {
  background: '#fff', border: '1px solid var(--invoicecloud-surface-default-border)',
  borderRadius: 10, padding: '10px 18px', fontSize: 14, fontWeight: 600, cursor: 'pointer',
};
