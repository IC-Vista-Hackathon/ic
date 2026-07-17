// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { PreviewTenant } from './types';

const provisionPreview = vi.fn<() => Promise<PreviewTenant>>();
const resetPreview = vi.fn<() => Promise<PreviewTenant>>();

vi.mock('./api', () => ({ api: { provisionPreview, resetPreview } }));
vi.mock('./insights', () => ({ trackEvent: vi.fn() }));

const tenant: PreviewTenant = {
  biller_id: 'b1',
  preview_biller_id: 'preview-b1',
  account_number: '4421',
  config_path: '/public/experiences/preview/preview-b1',
};

import { PreviewPane } from './PreviewPane';

afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('PreviewPane', () => {
  beforeEach(() => {
    provisionPreview.mockResolvedValue(tenant);
    resetPreview.mockResolvedValue({ ...tenant });
  });

  it('provisions the preview tenant and embeds the built PWA against it', async () => {
    render(<PreviewPane billerId="b1" device="desktop" previewBaseUrl="/pay/" />);

    // Loading state while the isolated tenant is seeded.
    expect(screen.getByTestId('preview-provisioning')).toBeDefined();

    const frame = await screen.findByTestId('preview-frame');
    expect(provisionPreview).toHaveBeenCalledWith('b1');
    // Same shipped bundle, scoped to the preview tenant via the ?preview param.
    expect(frame.getAttribute('src')).toContain('/pay/?preview=preview-b1');
    // Preview is clearly flagged as synthetic.
    expect(screen.getByTestId('preview-badge').textContent).toContain('synthetic');
  });

  it('resets the tenant and reloads the embedded preview', async () => {
    render(<PreviewPane billerId="b1" device="desktop" previewBaseUrl="/pay/" />);
    const before = (await screen.findByTestId('preview-frame')).getAttribute('src');

    await userEvent.click(screen.getByTestId('preview-reset'));

    await waitFor(() => expect(resetPreview).toHaveBeenCalledWith('b1'));
    // The reseed bumps the reload nonce so the iframe reloads the fresh tenant.
    await waitFor(() =>
      expect(screen.getByTestId('preview-frame').getAttribute('src')).not.toBe(before));
  });

  it('surfaces an error with retry when provisioning fails', async () => {
    provisionPreview.mockRejectedValueOnce(new Error('seed service down'));
    render(<PreviewPane billerId="b1" device="desktop" />);

    const error = await screen.findByTestId('preview-error');
    expect(error.textContent).toContain('seed service down');

    provisionPreview.mockResolvedValueOnce(tenant);
    await userEvent.click(screen.getByRole('button', { name: /try again/i }));
    await screen.findByTestId('preview-frame');
    expect(provisionPreview).toHaveBeenCalledTimes(2);
  });

  it('is unavailable without a saved biller', async () => {
    render(<PreviewPane billerId={null} device="desktop" />);
    expect(await screen.findByTestId('preview-unavailable')).toBeDefined();
    expect(provisionPreview).not.toHaveBeenCalled();
  });
});
