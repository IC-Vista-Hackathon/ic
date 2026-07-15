import { FormEvent, useEffect, useMemo, useState } from 'react';
import { billerSlug } from './billerSlug';
import { NoOpenBillError } from './errors';
import { trackEvent } from './insights';
import { ServicePaymentExperienceProvider, type PaymentQuote } from './provider';
import { logError, logEvent, observed } from './telemetry';
import { categorizeError } from './telemetryPolicy';
import { errorMessage, fetchWithTimeout, requestError } from './http';
import type { ExperienceDefinition, ExperiencePreferences, Invoice, PayerProfile, PaymentHistory, PaymentReceipt } from './types';

type Step = 'lookup' | 'method' | 'review' | 'complete';
type Page = 'payment' | 'history' | 'preferences';

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
  const [method, setMethod] = useState<'card'|'ach'>('card');
  const [autoPay, setAutoPay] = useState(false);
  const [paperless, setPaperless] = useState(false);
  const [receipt, setReceipt] = useState<PaymentReceipt>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [payerName, setPayerName] = useState('');
  const [payerEmail, setPayerEmail] = useState('');
  const [quotes, setQuotes] = useState<Record<string, PaymentQuote>>({});

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
  const acceptedMethods = useMemo(
    () => preferences.accepted_methods.filter(value => config?.enabled_payment_capabilities.includes(value)),
    [config?.enabled_payment_capabilities, preferences.accepted_methods]);
  // Server-side quotes: fees come from the Payment Service (same policy the charge applies),
  // never computed client-side. quote.totalCents === amount when the biller absorbs the fee.
  const quote = quotes[method];
  useEffect(() => {
    setQuotes({});
    if (!provider || !invoice) return;
    let cancelled = false;
    (['card', 'ach'] as const).filter(value => acceptedMethods.includes(value)).forEach(value =>
      provider.quote(invoice.id, value)
        .then(result => { if (!cancelled) setQuotes(previous => ({ ...previous, [value]: result })); })
        .catch(caught => logError('pwa.payment.quote_failed', caught, { method: value })));
    return () => { cancelled = true; };
  }, [acceptedMethods, provider, invoice]);
  const primaryAction = config?.ui?.actions.find(action => action.id === 'primary-payment-action');
  const schedulesPayment = primaryAction?.action === 1 || primaryAction?.action === 'schedule_payment';

  useEffect(() => { if (!acceptedMethods.includes(method)) setMethod(acceptedMethods.includes('ach') ? 'ach' : 'card'); }, [acceptedMethods, method]);

  async function lookup(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (!provider) return; setBusy(true); setError(''); try { const data = new FormData(event.currentTarget); const account = String(data.get('account')); const loaded = await provider.getInvoices(account); const current = loaded.find(item => item.status !== 'paid'); if (!current) throw new NoOpenBillError(); setInvoices(loaded); setInvoice(current); const profile = await provider.findPayer(account); setPayer(profile); if (profile) { setAutoPay(profile.preferences.autopay); setPaperless(profile.preferences.paperless); setPayerName(profile.name); setPayerEmail(profile.email); setPayments(await provider.getPayments(profile.payer_id)); } setStep('method'); trackEvent('pwa.bill_lookup', { outcome: 'found' }); } catch (caught) { setError(`Find bill: ${errorMessage(caught)}`); logError('pwa.invoice.lookup_failed', caught); if (caught instanceof NoOpenBillError) trackEvent('pwa.bill_lookup', { outcome: 'no_open_bill' }); else trackEvent('pwa.bill_lookup', { outcome: 'failed', error_category: categorizeError(caught) }); } finally { setBusy(false); } }
  async function pay() { if (!invoice || !provider) return; setBusy(true); setError(''); trackEvent('pwa.payment_submitted', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless }); try { const completed = await provider.pay({ invoiceId: invoice.id, method, autoPay, paperless, scheduledFor: schedulesPayment ? invoice.dueDate : undefined, payerName, payerEmail, accountNumber: invoice.accountNumber }); setReceipt(completed); if (completed.payerAccountId) { const profile = await provider.findPayer(invoice.accountNumber); setPayer(profile); if (profile) setPayments(await provider.getPayments(profile.payer_id)); } setStep('complete'); logEvent('pwa.payment.completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless }); trackEvent('pwa.payment_completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless }); } catch (caught) { setError(`Submit payment: ${errorMessage(caught)}`); logError('pwa.payment.failed', caught, { method, scheduled: schedulesPayment }); trackEvent('pwa.payment_failed', { method, scheduled: schedulesPayment, error_category: categorizeError(caught) }); } finally { setBusy(false); } }
  async function savePreferences() { if (!provider || !payer) return; setBusy(true); setError(''); try { const updated = await provider.updatePreferences(payer.payer_id, { ...payer.preferences, autopay: autoPay, paperless, channels: payer.preferences.channels.length ? payer.preferences.channels : ['email'], payment_day: autoPay ? (payer.preferences.payment_day ?? 15) : payer.preferences.payment_day }); setPayer({ ...payer, preferences: updated }); logEvent('pwa.preferences.saved', { payer_id: payer.payer_id }); trackEvent('pwa.preferences_saved', { outcome: 'saved' }); } catch (caught) { setError(`Save preferences: ${errorMessage(caught)}`); logError('pwa.preferences.save_failed', caught); trackEvent('pwa.preferences_saved', { outcome: 'failed', error_category: categorizeError(caught) }); } finally { setBusy(false); } }
  function navigate(next: Page) { setPage(next); }
  function selectMethod(next: 'card' | 'ach') { if (next !== method) trackEvent('pwa.payment_method_selected', { method: next }); setMethod(next); }
  function openReview() { trackEvent('pwa.review_opened', { method, scheduled: schedulesPayment }); setStep('review'); }
  function changeAutoPay(enabled: boolean) { setAutoPay(enabled); trackEvent('pwa.autopay_changed', { enabled }); }
  function changePaperless(enabled: boolean) { setPaperless(enabled); trackEvent('pwa.paperless_changed', { enabled }); }

  if (!config) return <main className="center"><div className="card config-state" aria-live="polite"><h1>{configState === 'error' ? 'Payment experience unavailable' : 'Loading payment experience'}</h1><p>{error || 'Loading your secure, branded payment page…'}</p>{configState === 'error' && <button onClick={() => setConfigAttempt(value => value + 1)}>Retry</button>}</div></main>;
  return <div className="app">
    <header style={{ background: config.brand.primary_color }}><div className="mark">{initials(config.brand.display_name)}</div><strong>{config.brand.display_name}</strong><span>Secure account services</span></header>
    <nav className="account-nav" aria-label="Account services"><button className={page === 'payment' ? 'active' : ''} onClick={() => navigate('payment')}>Pay Bill</button>{preferences.self_service_history && <button className={page === 'history' ? 'active' : ''} onClick={() => navigate('history')}>Account History</button>}{preferences.self_service_updates && <button className={page === 'preferences' ? 'active' : ''} onClick={() => navigate('preferences')}>Preferences</button>}</nav>
    <main><div className="intro"><p>Account services</p><h1>{page === 'payment' ? config.content.heading : page === 'history' ? 'Account history' : 'Communication preferences'}</h1><span>{page === 'payment' ? config.content.introduction : `Manage services for ${config.brand.display_name}.`}</span></div>{error && <div className="alert" role="alert">{error}</div>}
      {page === 'payment' && <>
        {step === 'lookup' && <form className="card" onSubmit={lookup}><h2>{preferences.guest_checkout_allowed ? 'Find your bill' : 'Access your account'}</h2><p className="card-copy">{preferences.guest_checkout_allowed ? 'No sign-in required. Enter the account number shown on your bill.' : 'Enter your account number to continue to the secure payment experience.'}</p><label>Account number<input name="account" required defaultValue="4421" autoComplete="off" /></label><button disabled={busy}>{busy ? 'Finding Bill…' : 'Continue'}</button></form>}
        {step === 'method' && invoice && <section className="card"><Bill invoice={invoice}/><h2>Choose how to pay</h2><div className="choices">{acceptedMethods.includes('card') && <button className={method === 'card' ? 'selected' : 'option'} onClick={() => selectMethod('card')}>Card <small>{quoteFeeText(quotes.card, invoice.amountCents)}</small></button>}{acceptedMethods.includes('ach') && <button className={method === 'ach' ? 'selected' : 'option'} onClick={() => selectMethod('ach')}>Bank Account <small>{quoteFeeText(quotes.ach, invoice.amountCents)}</small></button>}</div>{acceptedMethods.filter(value => value !== 'card' && value !== 'ach').length > 0 && <div className="method-chips">Also accepts {acceptedMethods.filter(value => value !== 'card' && value !== 'ach').map(humanize).join(' · ')}</div>}
          {(preferences.offer_autopay || preferences.offer_paperless) && <fieldset><legend>Optional preferences</legend>{preferences.offer_autopay && preferences.enroll_during_payment && <label className="check"><input type="checkbox" checked={autoPay} onChange={event => changeAutoPay(event.target.checked)}/><span><strong>Enroll in AutoPay</strong><small>Use this method for future bills. Cancel anytime.</small></span></label>}{preferences.offer_paperless && <label className="check"><input type="checkbox" checked={paperless} onChange={event => changePaperless(event.target.checked)}/><span><strong>Switch to Paperless Billing</strong><small>Receive bills electronically instead of by mail.</small></span></label>}{(autoPay || paperless) && <><label>Name<input value={payerName} onChange={event => setPayerName(event.target.value)} required/></label><label>Email<input type="email" value={payerEmail} onChange={event => setPayerEmail(event.target.value)} required/></label></>}</fieldset>}
          <button onClick={openReview} disabled={acceptedMethods.length === 0 || ((autoPay || paperless) && (!payerName || !payerEmail))}>Review Payment</button></section>}
        {step === 'review' && invoice && <section className="card"><h2>Review and confirm</h2><dl><div><dt>Bill amount</dt><dd>{money(invoice.amountCents)}</dd></div><div><dt>Service fee</dt><dd>{quoteFeeText(quote, invoice.amountCents)}</dd></div><div className="total"><dt>Total</dt><dd>{quote ? money(quote.totalCents) : '…'}</dd></div></dl>{schedulesPayment && <div className="notice">This payment will be scheduled for {new Date(invoice.dueDate).toLocaleDateString()}.</div>}{(autoPay || paperless) && <div className="notice">You chose: {[autoPay && 'AutoPay', paperless && 'Paperless Billing'].filter(Boolean).join(' and ')}.</div>}<p className="consent">Selecting “{primaryAction?.label ?? 'Pay Now'}” authorizes this {schedulesPayment ? 'scheduled' : 'one-time'} payment. Optional preferences are recorded separately.</p><div className="actions"><button className="back" onClick={() => setStep('method')}>Back</button><button disabled={busy || !quote} onClick={pay}>{busy ? 'Processing…' : (primaryAction?.label ?? (quote ? `Pay ${money(quote.totalCents)}` : 'Preparing quote…'))}</button></div></section>}
        {step === 'complete' && receipt && <section className="card success" aria-live="polite"><div className="success-icon">✓</div><h2>{schedulesPayment ? 'Payment scheduled' : 'Payment received'}</h2><p>Confirmation <strong>{receipt.confirmation}</strong></p><p>{money(receipt.totalCents)} {schedulesPayment ? 'scheduled' : 'paid'} using the configured provider.</p>{autoPay && <span className="pill">AutoPay requested</span>}{paperless && <span className="pill">Paperless requested</span>}</section>}
      </>}
      {page === 'history' && <section className="card"><h2>Recent account activity</h2>{invoice ? <>{invoices.map(item => <Bill key={item.id} invoice={item}/>)}{payments.map(payment => <div className="history-row" key={payment.payment_id}><span>Payment {payment.confirmation}<small>{new Date(payment.created_at).toLocaleDateString()}</small></span><strong>{money(payment.total_cents)}</strong></div>)}</> : <><p className="card-copy">Find your bill first to load account-specific activity.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
      {page === 'preferences' && <section className="card"><h2>Communication preferences</h2>{payer ? <><label className="check"><input type="checkbox" checked={autoPay} onChange={event => changeAutoPay(event.target.checked)}/><span><strong>AutoPay</strong><small>Automatically pay future bills.</small></span></label><label className="check"><input type="checkbox" checked={paperless} onChange={event => changePaperless(event.target.checked)}/><span><strong>Paperless Billing</strong><small>Receive bills electronically.</small></span></label><div className="preference-summary"><span>Payment reminders<strong>{payer.preferences.channels.map(humanize).join(', ') || reminderLabel(preferences.reminder_channel)}</strong></span></div><button disabled={busy} onClick={savePreferences}>{busy ? 'Saving…' : 'Save Preferences'}</button></> : <><p className="card-copy">Find your bill and register during checkout to manage account preferences.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
    </main>
    <footer><span>{config.content.support_text}</span><nav><a href={config.content.privacy_policy_url}>Privacy</a><a href={config.content.terms_of_service_url}>Terms</a></nav></footer>
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
function money(cents: number) { return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100); }
function initials(name: string) { return name.split(' ').map(word => word[0]).slice(0, 2).join(''); }
function validateConfig(value: unknown): ExperienceDefinition {
  if (!value || typeof value !== 'object') throw new Error('The payment experience configuration is invalid.');
  const candidate = value as Partial<ExperienceDefinition>;
  if (!candidate.biller_id || !candidate.brand?.display_name || !candidate.brand.primary_color || !candidate.content?.heading || !candidate.pwa?.name || !Array.isArray(candidate.enabled_payment_capabilities)) {
    throw new Error('The payment experience configuration is incomplete.');
  }
  if (candidate.preferences && (!Array.isArray(candidate.preferences.accepted_methods) || !candidate.preferences.preview || !Array.isArray(candidate.preferences.preview.enabled_scenarios))) {
    throw new Error('The payment experience preferences are invalid.');
  }
  return candidate as ExperienceDefinition;
}
