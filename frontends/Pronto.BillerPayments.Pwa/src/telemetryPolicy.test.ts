import { describe, expect, it } from 'vitest';
import { UiRequestError } from './http';
import { ALLOWED_EVENT_NAMES, categorizeError, sanitizeEvent } from './telemetryPolicy';

describe('sanitizeEvent', () => {
  it('drops events whose name is not allowlisted', () => {
    expect(sanitizeEvent('pwa.made_up_event', {})).toBeNull();
    expect(sanitizeEvent(undefined, {})).toBeNull();
    expect(sanitizeEvent(42, {})).toBeNull();
  });

  it('strips PII-shaped properties even on allowlisted events', () => {
    const sanitized = sanitizeEvent('pwa.payment_completed', {
      method: 'card',
      account_number: '4421',
      payer_name: 'Ada Lovelace',
      email: 'ada@example.com',
      amount_cents: 1234,
      confirmation: 'PMT-1',
      message: 'raw error text',
    });
    expect(sanitized).toEqual({ name: 'pwa.payment_completed', properties: { method: 'card' } });
  });

  it('strips values that fail their validator instead of exporting them', () => {
    const sanitized = sanitizeEvent('pwa.payment_method_selected', { method: 'crypto' });
    expect(sanitized).toEqual({ name: 'pwa.payment_method_selected', properties: {} });
  });

  it('stringifies booleans for the wire', () => {
    const sanitized = sanitizeEvent('pwa.autopay_changed', { enabled: true });
    expect(sanitized?.properties).toEqual({ enabled: 'true' });
  });

  it('rejects string impostors for boolean properties', () => {
    const sanitized = sanitizeEvent('pwa.autopay_changed', { enabled: 'yes definitely' });
    expect(sanitized?.properties).toEqual({});
  });

  it('accepts well-formed context properties on any event', () => {
    const sanitized = sanitizeEvent('pwa.session_started', {
      flow_id: 'a86d4e18-1f2c-4c4e-9d0e-6a1b2c3d4e5f',
      trace_id: 'a'.repeat(32),
      biller_slug: 'city-of-vista',
    });
    expect(sanitized?.properties).toEqual({
      flow_id: 'a86d4e18-1f2c-4c4e-9d0e-6a1b2c3d4e5f',
      trace_id: 'a'.repeat(32),
      biller_slug: 'city-of-vista',
    });
  });

  it('rejects malformed context values (nothing free-form rides along)', () => {
    const sanitized = sanitizeEvent('pwa.session_started', {
      flow_id: 'ada@example.com',
      trace_id: 'not-a-trace',
      biller_slug: 'Bad Slug!',
    });
    expect(sanitized?.properties).toEqual({});
  });

  it('covers the full lookup outcome enum', () => {
    for (const outcome of ['found', 'no_open_bill', 'failed']) {
      expect(sanitizeEvent('pwa.bill_lookup', { outcome })?.properties).toEqual({ outcome });
    }
  });

  it('allowlists exactly the semantic event set', () => {
    expect(ALLOWED_EVENT_NAMES.sort()).toEqual([
      'pwa.autopay_changed',
      'pwa.bill_lookup',
      'pwa.paperless_changed',
      'pwa.payment_completed',
      'pwa.payment_failed',
      'pwa.payment_method_selected',
      'pwa.payment_submitted',
      'pwa.preferences_saved',
      'pwa.review_opened',
      'pwa.session_started',
    ]);
  });
});

describe('categorizeError', () => {
  it('buckets HTTP failures by status without exporting text', () => {
    expect(categorizeError(new UiRequestError('secret detail', 404))).toBe('http_4xx');
    expect(categorizeError(new UiRequestError('secret detail', 503))).toBe('http_5xx');
  });

  it('buckets fetch network failures and timeouts', () => {
    expect(categorizeError(new TypeError('Failed to fetch'))).toBe('network');
    expect(categorizeError(new UiRequestError('The service could not be reached.', undefined, 'network_error'))).toBe('network');
    expect(categorizeError(new UiRequestError('The request timed out.', undefined, 'request_timeout'))).toBe('network');
  });

  it('falls back to unknown for anything else', () => {
    expect(categorizeError(new Error('whatever'))).toBe('unknown');
    expect(categorizeError('string error')).toBe('unknown');
  });
});
