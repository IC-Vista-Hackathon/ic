// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { billerSlug, configEndpoint, previewBillerId } from './billerSlug';

function setLocation(pathname: string, search = '') {
  window.history.replaceState({}, '', pathname + search);
}

describe('billerSlug', () => {
  afterEach(() => setLocation('/'));

  it('reads the slug from a /pay/{slug} path', () => {
    setLocation('/pay/acme-water');
    expect(billerSlug()).toBe('acme-water');
  });
});

describe('previewBillerId', () => {
  afterEach(() => setLocation('/'));

  it('is undefined for live payer traffic', () => {
    setLocation('/pay/acme-water');
    expect(previewBillerId()).toBeUndefined();
  });

  it('reads the preview tenant from the ?preview query param', () => {
    setLocation('/pay/acme-water', '?preview=preview-abc123');
    expect(previewBillerId()).toBe('preview-abc123');
  });

  it('treats an empty preview param as absent', () => {
    setLocation('/pay/acme-water', '?preview=');
    expect(previewBillerId()).toBeUndefined();
  });
});

describe('configEndpoint', () => {
  it('targets the published experience for the slug when not previewing', () => {
    expect(configEndpoint('acme-water', undefined)).toBe(
      '/api/public/experiences/acme-water');
  });

  it('targets the preview draft config when previewing', () => {
    expect(configEndpoint('acme-water', 'preview-abc123')).toBe(
      '/api/public/experiences/preview/preview-abc123');
  });
});
