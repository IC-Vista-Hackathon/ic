import { describe, expect, it } from 'vitest';
import { UiRequestError } from './http';
import { ALLOWED_EVENT_NAMES, categorizeError, sanitizeEvent } from './telemetryPolicy';

const FLOW_ID = 'a86d4e18-1f2c-4c4e-9d0e-6a1b2c3d4e5f';
const BILLER_ID = 'b1c2d3e4-5678-4abc-9def-0123456789ab';

describe('sanitizeEvent', () => {
  it('drops events whose name is not allowlisted', () => {
    expect(sanitizeEvent('studio.made_up_event', {})).toBeNull();
    expect(sanitizeEvent('pwa.session_started', {})).toBeNull();
    expect(sanitizeEvent(undefined, {})).toBeNull();
    expect(sanitizeEvent(42, {})).toBeNull();
  });

  it('strips PII-shaped properties even on allowlisted events', () => {
    const sanitized = sanitizeEvent('studio.draft_generated', {
      biller_id: BILLER_ID,
      display_name: 'City of Vista Utilities',
      website: 'https://cityofvista.example.com',
      email: 'ada@example.com',
      chat_text: 'Please build me a portal that looks like our old one',
      agent_response: 'Here is your generated experience...',
      amount_cents: 1234,
      heading: 'Pay your bill',
    });
    expect(sanitized).toEqual({ name: 'studio.draft_generated', properties: { biller_id: BILLER_ID } });
  });

  it('keeps chat_message_sent a count only — no text ever rides along', () => {
    const sanitized = sanitizeEvent('studio.chat_message_sent', {
      biller_id: BILLER_ID,
      text: 'Build a utilities payment experience',
      message: 'raw chat content',
    });
    expect(sanitized).toEqual({ name: 'studio.chat_message_sent', properties: { biller_id: BILLER_ID } });
  });

  it('strips values that fail their validator instead of exporting them', () => {
    expect(sanitizeEvent('studio.preview_opened', { device: 'watch' })?.properties).toEqual({});
    expect(sanitizeEvent('studio.validation_result', { outcome: 'exploded' })?.properties).toEqual({});
    expect(sanitizeEvent('studio.checklist_step_completed', { step: 'made_up' })?.properties).toEqual({});
  });

  it('accepts the device enum on preview_opened', () => {
    for (const device of ['desktop', 'mobile']) {
      expect(sanitizeEvent('studio.preview_opened', { device })?.properties).toEqual({ device });
    }
  });

  it('covers the full validation outcome enum', () => {
    for (const outcome of ['passed', 'warnings', 'failed']) {
      expect(sanitizeEvent('studio.validation_result', { outcome })?.properties).toEqual({ outcome });
    }
  });

  it('exports only an allowlisted error bucket on studio.client_error — never raw crash text', () => {
    for (const category of ['network', 'http_4xx', 'http_5xx', 'unknown']) {
      expect(sanitizeEvent('studio.client_error', { error_category: category })?.properties).toEqual({ error_category: category });
    }
    const sanitized = sanitizeEvent('studio.client_error', {
      error_category: 'unknown',
      message: 'TypeError: cannot read properties of undefined',
      component_stack: '    at App (App.tsx:42)',
    });
    expect(sanitized).toEqual({ name: 'studio.client_error', properties: { error_category: 'unknown' } });
  });

  it('covers the full checklist step enum', () => {
    for (const step of ['vertical', 'business_location', 'brand_details', 'import_data', 'customer_experience']) {
      expect(sanitizeEvent('studio.checklist_step_completed', { step })?.properties).toEqual({ step });
    }
  });

  it('buckets publish_failed by error category only', () => {
    expect(sanitizeEvent('studio.publish_failed', { error_category: 'http_5xx' })?.properties).toEqual({ error_category: 'http_5xx' });
    expect(sanitizeEvent('studio.publish_failed', { error_category: 'boom', message: 'raw error' })?.properties).toEqual({});
  });

  it('accepts well-formed context properties on any event', () => {
    const sanitized = sanitizeEvent('studio.session_started', {
      flow_id: FLOW_ID,
      trace_id: 'a'.repeat(32),
      biller_id: BILLER_ID,
    });
    expect(sanitized?.properties).toEqual({ flow_id: FLOW_ID, trace_id: 'a'.repeat(32), biller_id: BILLER_ID });
  });

  it('rejects malformed context values (nothing free-form rides along)', () => {
    const sanitized = sanitizeEvent('studio.session_started', {
      flow_id: 'ada@example.com',
      trace_id: 'not-a-trace',
      biller_id: 'City of Vista',
    });
    expect(sanitized?.properties).toEqual({});
  });

  it('allowlists exactly the semantic event set', () => {
    expect(ALLOWED_EVENT_NAMES.sort()).toEqual([
      'studio.checklist_step_completed',
      'studio.chat_message_sent',
      'studio.client_error',
      'studio.draft_generated',
      'studio.onboarding_started',
      'studio.preview_opened',
      'studio.publish_completed',
      'studio.publish_failed',
      'studio.publish_requested',
      'studio.purchase_completed',
      'studio.purchase_started',
      'studio.session_started',
      'studio.validation_result',
    ].sort());
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
