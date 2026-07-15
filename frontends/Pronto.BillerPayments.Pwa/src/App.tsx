import { FormEvent, useEffect, useMemo, useRef, useState } from 'react';
import { billerSlug } from './billerSlug';
import { NoOpenBillError } from './errors';
import { randomId } from './id';
import { trackEvent } from './insights';
import { ServicePaymentExperienceProvider, type PaymentQuote, type AssistantRecommendation } from './provider';
import { Header, Intro, Footer } from './skin';
import { logError, logEvent, observed } from './telemetry';
import { categorizeError } from './telemetryPolicy';
import { errorMessage, fetchWithTimeout, requestError } from './http';
import type { ExperienceDefinition, ExperiencePreferences, Invoice, PayerProfile, PaymentHistory, PaymentMethod, PaymentReceipt } from './types';

type Step = 'lookup' | 'method' | 'review' | 'complete';
type Page = 'payment' | 'history' | 'preferences';
const PAYMENT_METHODS: PaymentMethod[] = ['card', 'ach'];

export function App() {
  const [config, setConfig] = useState<ExperienceDefinition>();
  const [configState, setConfigState] = useState<'loading'|'ready'|'error'>('loading');
  const [configAttempt, setConfigAttempt] = useState(0);
  const [invoice, setInvoice] = useState<Invoice>();
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [payer, setPayer] = useState<PayerProfile>();
  const [payments, setPayments] = useState<PaymentHistory[]>([]);
  const [step, setStep] = useState<Step>('lookup');
  const [page, setPage] = useState<Page>('payment');
  const [method, setMethod] = useState<PaymentMethod>('card');
  const [autoPay, setAutoPay] = useState(false);
  const [paperless, setPaperless] = useState(false);
  const [receipt, setReceipt] = useState<PaymentReceipt>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [payerName, setPayerName] = useState('');
  const [payerEmail, setPayerEmail] = useState('');
  const [quotes, setQuotes] = useState<Record<string, PaymentQuote>>({});
  const [quoteErrors, setQuoteErrors] = useState<Partial<Record<PaymentMethod, string>>>({});
  const [quoteAttempt, setQuoteAttempt] = useState(0);
  const [recommendation, setRecommendation] = useState<AssistantRecommendation>();
  const [assistantState, setAssistantState] = useState<'idle'|'thinking'|'ready'|'error'>('idle');
  const [assistantAttempt, setAssistantAttempt] = useState(0);
  const paymentInFlight = useRef(false);
  const paymentIdempotencyKey = useRef<string | undefined>(undefined);

  useEffect(() => {
    setConfigState('loading'); setError('');
    const slug = billerSlug(); const configUrl = import.meta.env.VITE_CONFIG_URL ?? `/api/public/experiences/${encodeURIComponent(slug)}`;
    const manifest = document.querySelector<HTMLLinkElement>('#experience-manifest'); if (manifest) manifest.href = `/api/public/experiences/${encodeURIComponent(slug)}/manifest.webmanifest`;
    observed('pwa.config.load', async () => { const response = await fetchWithTimeout(configUrl, { cache: 'no-store' }); if (!response.ok) throw await requestError(response, 'Experience configuration is unavailable.'); return validateConfig(await response.json()); })
      .then(value => { setConfig(value); setConfigState('ready'); document.title = value.pwa.name; document.documentElement.style.setProperty('--brand', value.brand.primary_color); document.documentElement.style.setProperty('--brand-secondary', value.brand.secondary_color); if (value.brand.font_family) document.documentElement.style.setProperty('--brand-font', value.brand.font_family); })
      .catch(caught => { setConfigState('error'); setError(`Load payment experience: ${errorMessage(caught)}`); logError('pwa.config.failed', caught, { biller_slug: slug }); });
  }, [configAttempt]);

  const preferences = useMemo(() => experiencePreferences(config), [config]);
  const provider = useMemo(() => config ? new ServicePaymentExperienceProvider(config.biller_id) : undefined, [config]);
  const acceptedMethods = useMemo<PaymentMethod[]>(
    () => preferences.accepted_methods
      .filter(isPaymentMethod)
      .filter(value => config?.enabled_payment_capabilities.includes(value)),
    [config?.enabled_payment_capabilities, preferences.accepted_methods]);
  // Server-side quotes: fees come from the Payment Service (same policy the charge applies),
  // never computed client-side. quote.totalCents === amount when the biller absorbs the fee.
  const quote = quotes[method];
  useEffect(() => {
    setQuotes({});
    setQuoteErrors({});
    if (!provider || !invoice) return;
    let cancelled = false;
    PAYMENT_METHODS.filter(value => acceptedMethods.includes(value)).forEach(value =>
      provider.quote(invoice.id, value)
        .then(result => { if (!cancelled) setQuotes(previous => ({ ...previous, [value]: result })); })
        .catch(caught => {
          logError('pwa.payment.quote_failed', caught, { method: value });
          if (!cancelled) setQuoteErrors(previous => ({ ...previous, [value]: errorMessage(caught) }));
        }));
    return () => { cancelled = true; };
  }, [acceptedMethods, provider, invoice, quoteAttempt]);
  // Payer-side agent turn: once a bill is in hand, ask the assistant to read it and recommend a
  // method + timing. Advisory only — it never moves money and the payer stays in control below.
  useEffect(() => {
    if (!provider || !invoice) { setRecommendation(undefined); setAssistantState('idle'); return; }
    let cancelled = false;
    setAssistantState('thinking'); setRecommendation(undefined);
    provider.askAssistant(invoice.id, invoice.accountNumber)
      .then(result => { if (!cancelled) { setRecommendation(result); setAssistantState('ready'); trackEvent('pwa.assistant_recommended', { method: isPaymentMethod(result.method) ? result.method : 'card', scheduled: !!result.scheduledFor }); } })
      .catch(caught => { if (!cancelled) { setAssistantState('error'); logError('pwa.assistant.failed', caught); } });
    return () => { cancelled = true; };
  }, [provider, invoice, assistantAttempt]);
  const primaryAction = config?.ui?.actions.find(action => action.id === 'primary-payment-action');
  const schedulesPayment = primaryAction?.action === 1 || primaryAction?.action === 'schedule_payment';

  useEffect(() => { if (!acceptedMethods.includes(method)) setMethod(acceptedMethods.includes('ach') ? 'ach' : 'card'); }, [acceptedMethods, method]);

  async function lookup(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (!provider) return; setBusy(true); setError(''); paymentIdempotencyKey.current = undefined; try { const data = new FormData(event.currentTarget); const account = String(data.get('account')); const loaded = await provider.getInvoices(account); const current = loaded.find(item => item.status !== 'paid'); if (!current) throw new NoOpenBillError(); setInvoices(loaded); setInvoice(current); const profile = await provider.findPayer(account); setPayer(profile); if (profile) { setAutoPay(profile.preferences.autopay); setPaperless(profile.preferences.paperless); setPayerName(profile.name); setPayerEmail(profile.email); setPayments(await provider.getPayments(profile.payer_id)); } setStep('method'); trackEvent('pwa.bill_lookup', { outcome: 'found' }); } catch (caught) { setError(`Find bill: ${errorMessage(caught)}`); logError('pwa.invoice.lookup_failed', caught); if (caught instanceof NoOpenBillError) trackEvent('pwa.bill_lookup', { outcome: 'no_open_bill' }); else trackEvent('pwa.bill_lookup', { outcome: 'failed', error_category: categorizeError(caught) }); } finally { setBusy(false); } }
  async function pay() {
    if (!invoice || !provider || paymentInFlight.current) return;
    paymentInFlight.current = true;
    paymentIdempotencyKey.current ??= randomId();
    setBusy(true);
    setError('');
    trackEvent('pwa.payment_submitted', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
    try {
      const completed = await provider.pay({
        invoiceId: invoice.id,
        method,
        autoPay,
        paperless,
        scheduledFor: schedulesPayment ? invoice.dueDate : undefined,
        payerName,
        payerEmail,
        accountNumber: invoice.accountNumber,
        idempotencyKey: paymentIdempotencyKey.current,
      });
      paymentIdempotencyKey.current = undefined;
      setReceipt(completed);
      setStep('complete');
      if (completed.preferenceUpdateFailed) {
        setError('Payment completed, but optional preferences could not be saved. You can retry them from Preferences.');
      }
      logEvent('pwa.payment.completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
      trackEvent('pwa.payment_completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
      if (completed.payerAccountId) {
        try {
          const profile = await provider.findPayer(invoice.accountNumber);
          setPayer(profile);
          if (profile) setPayments(await provider.getPayments(profile.payer_id));
        } catch (caught) {
          logError('pwa.payment.history_refresh_failed', caught, { method });
        }
      }
    } catch (caught) {
      setError(`Submit payment: ${errorMessage(caught)}`);
      logError('pwa.payment.failed', caught, { method, scheduled: schedulesPayment });
      trackEvent('pwa.payment_failed', { method, scheduled: schedulesPayment, error_category: categorizeError(caught) });
    } finally {
      paymentInFlight.current = false;
      setBusy(false);
    }
  }
  async function savePreferences() { if (!provider || !payer) return; setBusy(true); setError(''); try { const updated = await provider.updatePreferences(payer.payer_id, { ...payer.preferences, autopay: autoPay, paperless, channels: payer.preferences.channels.length ? payer.preferences.channels : ['email'], payment_day: autoPay ? (payer.preferences.payment_day ?? 15) : payer.preferences.payment_day }); setPayer({ ...payer, preferences: updated }); logEvent('pwa.preferences.saved', { outcome: 'saved' }); trackEvent('pwa.preferences_saved', { outcome: 'saved' }); } catch (caught) { setError(`Save preferences: ${errorMessage(caught)}`); logError('pwa.preferences.save_failed', caught); trackEvent('pwa.preferences_saved', { outcome: 'failed', error_category: categorizeError(caught) }); } finally { setBusy(false); } }
  function navigate(next: Page) { setPage(next); }
  function selectMethod(next: PaymentMethod) { if (next !== method) trackEvent('pwa.payment_method_selected', { method: next }); paymentIdempotencyKey.current = undefined; setMethod(next); }
  function applyRecommendation() { if (recommendation && isPaymentMethod(recommendation.method) && acceptedMethods.includes(recommendation.method)) selectMethod(recommendation.method); }
  function openReview() { trackEvent('pwa.review_opened', { method, scheduled: schedulesPayment }); setStep('review'); }
  function changeAutoPay(enabled: boolean) { setAutoPay(enabled); trackEvent('pwa.autopay_changed', { enabled }); }
  function changePaperless(enabled: boolean) { setPaperless(enabled); trackEvent('pwa.paperless_changed', { enabled }); }

  if (!config) return <main className="center"><div className="card config-state" aria-live="polite"><h1>{configState === 'error' ? 'Payment experience unavailable' : 'Loading payment experience'}</h1><p>{error || 'Loading your secure, branded payment page…'}</p>{configState === 'error' && <button onClick={() => setConfigAttempt(value => value + 1)}>Retry</button>}</div></main>;
  return <div className="app">
    <Header brand={config.brand} />
    <nav className="account-nav" aria-label="Account services"><button className={page === 'payment' ? 'active' : ''} onClick={() => navigate('payment')}>Pay Bill</button>{preferences.self_service_history && <button className={page === 'history' ? 'active' : ''} onClick={() => navigate('history')}>Account History</button>}{preferences.self_service_updates && <button className={page === 'preferences' ? 'active' : ''} onClick={() => navigate('preferences')}>Preferences</button>}</nav>
    <main><Intro eyebrow="Account services" heading={page === 'payment' ? config.content.heading : page === 'history' ? 'Account history' : 'Communication preferences'} subheading={page === 'payment' ? config.content.introduction : `Manage services for ${config.brand.display_name}.`} />{error && <div className="alert" role="alert" data-testid="error">{error}</div>}
      {page === 'payment' && <>
        {config.billing?.categories.length ? <section className="card" aria-label="Billing options"><h2>Billing options</h2>{config.billing.categories.map(category => <div className="history-row" key={category.id}><span><strong>{category.display_name}</strong><small>{category.cadence_label} · {category.state_summary}</small></span><strong>{paymentTermsLabel(category.payment_mode, category.maximum_installments)}</strong></div>)}</section> : null}
        {step === 'lookup' && <form className="card" onSubmit={lookup}><h2>{preferences.guest_checkout_allowed ? 'Find your bill' : 'Access your account'}</h2><p className="card-copy">{preferences.guest_checkout_allowed ? 'No sign-in required. Enter the account number shown on your bill.' : 'Enter your account number to continue to the secure payment experience.'}</p><label>Account number<input name="account" data-testid="account-input" required defaultValue="4421" autoComplete="off" /></label><button data-testid="lookup-submit" disabled={busy}>{busy ? 'Finding Bill…' : 'Continue'}</button></form>}
        {step === 'method' && invoice && assistantState !== 'idle' && <section className="card assistant" data-testid="assistant" aria-label="Payment assistant">
          <div className="assistant-head"><span className="assistant-avatar" aria-hidden="true">✦</span><div><strong>Payment assistant</strong><small>Reviews your bill and suggests a way to pay. You decide.</small></div></div>
          {assistantState === 'thinking' && <p className="assistant-reply" aria-live="polite">Reviewing your bill and comparing payment options…</p>}
          {assistantState === 'error' && <div className="alert" role="alert">The assistant is unavailable right now — you can still choose a method below. <button type="button" onClick={() => setAssistantAttempt(value => value + 1)}>Retry</button></div>}
          {assistantState === 'ready' && recommendation && <><p className="assistant-reply" aria-live="polite" data-testid="assistant-reply">{recommendation.reply}</p>
            <div className="assistant-plan"><span>Recommended</span><strong>{methodLabel(recommendation.method)} · {money(recommendation.totalCents)}{recommendation.scheduledFor ? ` · scheduled ${new Date(recommendation.scheduledFor).toLocaleDateString()}` : ' · pay now'}</strong></div>
            {isPaymentMethod(recommendation.method) && acceptedMethods.includes(recommendation.method) && method !== recommendation.method && <button type="button" data-testid="assistant-apply" onClick={applyRecommendation}>Use {methodLabel(recommendation.method)}</button>}</>}
        </section>}
        {step === 'method' && invoice && <section className="card"><Bill invoice={invoice}/><h2>Choose how to pay</h2><div className="choices">{acceptedMethods.includes('card') && <button data-testid="method-card" className={method === 'card' ? 'selected' : 'option'} onClick={() => selectMethod('card')}>Card <small>{quoteFeeText(quotes.card, invoice.amountCents)}</small></button>}{acceptedMethods.includes('ach') && <button data-testid="method-ach" className={method === 'ach' ? 'selected' : 'option'} onClick={() => selectMethod('ach')}>Bank Account <small>{quoteFeeText(quotes.ach, invoice.amountCents)}</small></button>}</div>
          {quoteErrors[method] && <div className="alert" role="alert" data-testid="quote-error">We couldn’t prepare this payment method. {quoteErrors[method]} <button type="button" onClick={() => setQuoteAttempt(value => value + 1)}>Retry quote</button></div>}
          {(preferences.offer_autopay || preferences.offer_paperless) && <fieldset><legend>Optional preferences</legend>{preferences.offer_autopay && preferences.enroll_during_payment && <label className="check"><input type="checkbox" checked={autoPay} onChange={event => changeAutoPay(event.target.checked)}/><span><strong>Enroll in AutoPay</strong><small>Use this method for future bills. Cancel anytime.</small></span></label>}{preferences.offer_paperless && <label className="check"><input type="checkbox" checked={paperless} onChange={event => changePaperless(event.target.checked)}/><span><strong>Switch to Paperless Billing</strong><small>Receive bills electronically instead of by mail.</small></span></label>}{(autoPay || paperless) && <><label>Name<input value={payerName} onChange={event => setPayerName(event.target.value)} required/></label><label>Email<input type="email" value={payerEmail} onChange={event => setPayerEmail(event.target.value)} required/></label></>}</fieldset>}
          <button data-testid="review-submit" onClick={openReview} disabled={!quote || !!quoteErrors[method] || ((autoPay || paperless) && (!payerName || !payerEmail))}>Review Payment</button></section>}
        {step === 'review' && invoice && <section className="card"><h2>Review and confirm</h2><dl><div><dt>Bill amount</dt><dd>{money(invoice.amountCents)}</dd></div><div><dt>Service fee</dt><dd>{quoteFeeText(quote, invoice.amountCents)}</dd></div><div className="total"><dt>Total</dt><dd>{quote ? money(quote.totalCents) : '…'}</dd></div></dl>{schedulesPayment && <div className="notice">This payment will be scheduled for {new Date(invoice.dueDate).toLocaleDateString()}.</div>}{(autoPay || paperless) && <div className="notice">You chose: {[autoPay && 'AutoPay', paperless && 'Paperless Billing'].filter(Boolean).join(' and ')}.</div>}<p className="consent">Selecting “{primaryAction?.label ?? 'Pay Now'}” authorizes this {schedulesPayment ? 'scheduled' : 'one-time'} payment. Optional preferences are recorded separately.</p><div className="actions"><button className="back" onClick={() => setStep('method')}>Back</button><button data-testid="pay-submit" disabled={busy || !quote} onClick={pay}>{busy ? 'Processing…' : (primaryAction?.label ?? (quote ? `Pay ${money(quote.totalCents)}` : 'Quote unavailable'))}</button></div></section>}
        {step === 'complete' && receipt && <section className="card success" data-testid="payment-confirmation" aria-live="polite"><div className="success-icon">✓</div><h2>{schedulesPayment ? 'Payment scheduled' : 'Payment received'}</h2><p>Confirmation <strong data-testid="confirmation-code">{receipt.confirmation}</strong></p><p>{money(receipt.totalCents)} {schedulesPayment ? 'scheduled' : 'paid'} using the configured provider.</p>{autoPay && <span className="pill">AutoPay requested</span>}{paperless && <span className="pill">Paperless requested</span>}</section>}
      </>}
      {page === 'history' && <section className="card"><h2>Recent account activity</h2>{invoice ? <>{invoices.map(item => <Bill key={item.id} invoice={item}/>)}{payments.map(payment => <div className="history-row" key={payment.payment_id}><span>Payment {payment.confirmation}<small>{new Date(payment.created_at).toLocaleDateString()}</small></span><strong>{money(payment.total_cents)}</strong></div>)}</> : <><p className="card-copy">Find your bill first to load account-specific activity.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
      {page === 'preferences' && <section className="card"><h2>Communication preferences</h2>{payer ? <><label className="check"><input type="checkbox" checked={autoPay} onChange={event => changeAutoPay(event.target.checked)}/><span><strong>AutoPay</strong><small>Automatically pay future bills.</small></span></label><label className="check"><input type="checkbox" checked={paperless} onChange={event => changePaperless(event.target.checked)}/><span><strong>Paperless Billing</strong><small>Receive bills electronically.</small></span></label><div className="preference-summary"><span>Payment reminders<strong>{payer.preferences.channels.map(humanize).join(', ') || reminderLabel(preferences.reminder_channel)}</strong></span></div><button disabled={busy} onClick={savePreferences}>{busy ? 'Saving…' : 'Save Preferences'}</button></> : <><p className="card-copy">Find your bill and register during checkout to manage account preferences.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
    </main>
    <Footer brand={config.brand} content={config.content} />
  </div>;
}

function Bill({ invoice }: { invoice: Invoice }) { return <div className="bill"><div><span>{invoice.description}</span><small>Due {new Date(invoice.dueDate).toLocaleDateString()}</small></div><strong>{money(invoice.amountCents)}</strong></div>; }
function experiencePreferences(config?: ExperienceDefinition): ExperiencePreferences { return config?.preferences ?? { guest_checkout_allowed: true, offer_autopay: true, enroll_during_payment: true, offer_paperless: true, reminder_channel: 'both', accepted_methods: config?.enabled_payment_capabilities ?? ['card', 'ach'], self_service_history: true, self_service_updates: true, fee_handling: 'mixed', preview: { default_device: 'desktop', enabled_scenarios: ['payment', 'history', 'communication', 'complex'] } }; }
function quoteFeeText(quote: PaymentQuote | undefined, amountCents: number) {
  if (!quote) return '…';
  return quote.totalCents === amountCents ? 'No payer fee' : `${money(quote.totalCents - amountCents)} fee`;
}
function reminderLabel(value: number | string) { return typeof value === 'number' ? ['Email', 'Text (SMS)', 'Both', 'None'][value] : humanize(value); }
function humanize(value: string) { return value.replaceAll('_', ' ').replace(/\b\w/g, match => match.toUpperCase()); }
function methodLabel(method: string) { return method === 'ach' ? 'Bank account (ACH)' : method === 'card' ? 'Card' : humanize(method); }
function paymentTermsLabel(mode: string | number | null | undefined, maximum?: number | null) { const installments = mode === 'installments_allowed' || mode === 1; if (!installments) return 'Pay in full'; return maximum ? `Up to ${maximum} installments` : 'Installments available'; }
function money(cents: number) { return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100); }
function validateConfig(value: unknown): ExperienceDefinition {
  if (!isRecord(value)) throw new Error('The payment experience configuration is invalid.');
  const brand = value.brand;
  const content = value.content;
  const pwa = value.pwa;
  const capabilities = value.enabled_payment_capabilities;
  if (
    !hasString(value, 'schema_version') ||
    !hasString(value, 'biller_id') ||
    !isRecord(brand) ||
    !hasString(brand, 'display_name') ||
    !hasString(brand, 'primary_color') ||
    !hasString(brand, 'secondary_color') ||
    !hasNullableString(brand, 'font_family') ||
    !isRecord(content) ||
    !hasString(content, 'heading') ||
    !hasString(content, 'introduction') ||
    !hasString(content, 'support_text') ||
    !hasString(content, 'privacy_policy_url', true) ||
    !hasString(content, 'terms_of_service_url', true) ||
    !isRecord(pwa) ||
    !hasString(pwa, 'name') ||
    !hasString(pwa, 'short_name') ||
    !hasString(pwa, 'theme_color') ||
    !hasString(pwa, 'background_color') ||
    !isStringArray(capabilities)
  ) {
    throw new Error('The payment experience configuration is incomplete.');
  }
  if (value.ui !== undefined && !isValidUi(value.ui)) {
    throw new Error('The payment experience interface configuration is invalid.');
  }
  if (value.preferences !== undefined && !isValidPreferences(value.preferences)) {
    throw new Error('The payment experience preferences are invalid.');
  }
  if (value.billing !== undefined && !isValidBilling(value.billing)) {
    throw new Error('The payment experience billing options are invalid.');
  }
  const accepted = isRecord(value.preferences) && isStringArray(value.preferences.accepted_methods)
    ? value.preferences.accepted_methods
    : capabilities;
  if (!PAYMENT_METHODS.some(method => capabilities.includes(method) && accepted.includes(method))) {
    throw new Error('The payment experience does not include a supported payment method.');
  }
  return value as unknown as ExperienceDefinition;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasString(value: Record<string, unknown>, key: string, allowEmpty = false): boolean {
  const field = value[key];
  return typeof field === 'string' && (allowEmpty || field.trim().length > 0);
}

function hasNullableString(value: Record<string, unknown>, key: string): boolean {
  return value[key] === null || hasString(value, key);
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(item => typeof item === 'string' && item.trim().length > 0);
}

function isPaymentMethod(value: string): value is PaymentMethod {
  return value === 'card' || value === 'ach';
}

function isValidUi(value: unknown): boolean {
  if (!isRecord(value) || !hasString(value, 'layout') || !isRecord(value.theme) || !Array.isArray(value.sections) || !Array.isArray(value.actions)) return false;
  if (!hasString(value.theme, 'density') || !hasString(value.theme, 'radius') || !hasString(value.theme, 'surface')) return false;
  const sectionsValid = value.sections.every(section =>
    isRecord(section) &&
    hasString(section, 'id') &&
    hasString(section, 'type') &&
    hasString(section, 'variant') &&
    typeof section.visible === 'boolean');
  const actionsValid = value.actions.every(action =>
    isRecord(action) &&
    hasString(action, 'id') &&
    hasString(action, 'label') &&
    (typeof action.action === 'string' || typeof action.action === 'number') &&
    hasString(action, 'variant'));
  return sectionsValid && actionsValid;
}

function isValidPreferences(value: unknown): boolean {
  if (!isRecord(value) || !isRecord(value.preview)) return false;
  return (
    typeof value.guest_checkout_allowed === 'boolean' &&
    typeof value.offer_autopay === 'boolean' &&
    typeof value.enroll_during_payment === 'boolean' &&
    typeof value.offer_paperless === 'boolean' &&
    (typeof value.reminder_channel === 'string' || typeof value.reminder_channel === 'number') &&
    isStringArray(value.accepted_methods) &&
    typeof value.self_service_history === 'boolean' &&
    typeof value.self_service_updates === 'boolean' &&
    (typeof value.fee_handling === 'string' || typeof value.fee_handling === 'number') &&
    hasString(value.preview, 'default_device') &&
    isStringArray(value.preview.enabled_scenarios)
  );
}

function isValidBilling(value: unknown): boolean {
  if (!isRecord(value) || !Array.isArray(value.categories)) return false;
  return value.categories.every(category =>
    isRecord(category) &&
    hasString(category, 'id') &&
    hasString(category, 'display_name') &&
    hasString(category, 'cadence_label') &&
    hasString(category, 'state_summary') &&
    (category.payment_mode === null || category.payment_mode === undefined || typeof category.payment_mode === 'string' || typeof category.payment_mode === 'number') &&
    (category.maximum_installments === null || category.maximum_installments === undefined || typeof category.maximum_installments === 'number'));
}
