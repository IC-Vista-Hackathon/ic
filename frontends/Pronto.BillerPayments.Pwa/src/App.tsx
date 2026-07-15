import { FormEvent, useEffect, useMemo, useState } from 'react';
import { ServicePaymentExperienceProvider } from './provider';
import { Header, Intro, Footer } from './skin';
import { logError, logEvent, observed } from './telemetry';
import type { ExperienceDefinition, ExperiencePreferences, Invoice, PayerProfile, PaymentHistory, PaymentReceipt } from './types';

type Step = 'lookup' | 'method' | 'review' | 'complete';
type Page = 'payment' | 'history' | 'preferences';

export function App() {
  const [config, setConfig] = useState<ExperienceDefinition>();
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

  useEffect(() => {
    const slug = billerSlug(); const configUrl = import.meta.env.VITE_CONFIG_URL ?? `/api/public/experiences/${encodeURIComponent(slug)}`;
    const manifest = document.querySelector<HTMLLinkElement>('#experience-manifest'); if (manifest) manifest.href = `/api/public/experiences/${encodeURIComponent(slug)}/manifest.webmanifest`;
    observed('pwa.config.load', async () => { const response = await fetch(configUrl, { cache: 'no-store' }); if (!response.ok) throw new Error('Experience configuration is unavailable.'); return response.json() as Promise<ExperienceDefinition>; })
      .then(value => { setConfig(value); document.title = value.pwa.name; document.documentElement.style.setProperty('--brand', value.brand.primary_color); document.documentElement.style.setProperty('--brand-secondary', value.brand.secondary_color); if (value.brand.font_family) document.documentElement.style.setProperty('--brand-font', value.brand.font_family); })
      .catch(caught => { setError(toMessage(caught)); logError('pwa.config.failed', caught, { biller_slug: slug }); });
  }, []);

  const preferences = useMemo(() => experiencePreferences(config), [config]);
  const feeHandling = preferences.fee_handling;
  const fee = useMemo(() => isAbsorbed(feeHandling) ? 0 : method === 'card' ? 250 : 95, [method, feeHandling]);
  const provider = useMemo(() => config ? new ServicePaymentExperienceProvider(config.biller_id) : undefined, [config]);
  const primaryAction = config?.ui?.actions.find(action => action.id === 'primary-payment-action');
  const schedulesPayment = primaryAction?.action === 1 || primaryAction?.action === 'schedule_payment';
  const acceptedMethods = preferences.accepted_methods.filter(value => config?.enabled_payment_capabilities.includes(value));

  useEffect(() => { if (!acceptedMethods.includes(method)) setMethod(acceptedMethods.includes('ach') ? 'ach' : 'card'); }, [acceptedMethods, method]);

  async function lookup(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (!provider) return; setBusy(true); setError(''); try { const data = new FormData(event.currentTarget); const account = String(data.get('account')); const loaded = await provider.getInvoices(account); const current = loaded.find(item => item.status !== 'paid'); if (!current) throw new Error('No open bill was found for that account.'); setInvoices(loaded); setInvoice(current); const profile = await provider.findPayer(account); setPayer(profile); if (profile) { setAutoPay(profile.preferences.autopay); setPaperless(profile.preferences.paperless); setPayerName(profile.name); setPayerEmail(profile.email); setPayments(await provider.getPayments(profile.payer_id)); } setStep('method'); } catch (caught) { setError(toMessage(caught)); logError('pwa.invoice.lookup_failed', caught); } finally { setBusy(false); } }
  async function pay() { if (!invoice || !provider) return; setBusy(true); setError(''); try { const completed = await provider.pay({ invoiceId: invoice.id, method, autoPay, paperless, scheduledFor: schedulesPayment ? invoice.dueDate : undefined, payerName, payerEmail, accountNumber: invoice.accountNumber }); setReceipt(completed); if (completed.payerAccountId) { const profile = await provider.findPayer(invoice.accountNumber); setPayer(profile); if (profile) setPayments(await provider.getPayments(profile.payer_id)); } setStep('complete'); logEvent('pwa.payment.completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless }); } catch (caught) { setError(toMessage(caught)); logError('pwa.payment.failed', caught, { method, scheduled: schedulesPayment }); } finally { setBusy(false); } }
  async function savePreferences() { if (!provider || !payer) return; setBusy(true); setError(''); try { const updated = await provider.updatePreferences(payer.payer_id, { ...payer.preferences, autopay: autoPay, paperless, channels: payer.preferences.channels.length ? payer.preferences.channels : ['email'], payment_day: autoPay ? (payer.preferences.payment_day ?? 15) : payer.preferences.payment_day }); setPayer({ ...payer, preferences: updated }); logEvent('pwa.preferences.saved', { payer_id: payer.payer_id }); } catch (caught) { setError(toMessage(caught)); logError('pwa.preferences.save_failed', caught); } finally { setBusy(false); } }
  function navigate(next: Page) { setPage(next); setError(''); }

  if (!config) return <main className="center"><div className="card" aria-live="polite">{error || 'Loading your payment experience…'}</div></main>;
  return <div className="app">
    <Header brand={config.brand} />
    <nav className="account-nav" aria-label="Account services"><button className={page === 'payment' ? 'active' : ''} onClick={() => navigate('payment')}>Pay Bill</button>{preferences.self_service_history && <button className={page === 'history' ? 'active' : ''} onClick={() => navigate('history')}>Account History</button>}{preferences.self_service_updates && <button className={page === 'preferences' ? 'active' : ''} onClick={() => navigate('preferences')}>Preferences</button>}</nav>
    <main><Intro eyebrow="Account services" heading={page === 'payment' ? config.content.heading : page === 'history' ? 'Account history' : 'Communication preferences'} subheading={page === 'payment' ? config.content.introduction : `Manage services for ${config.brand.display_name}.`} />{error && <div className="alert" role="alert" data-testid="error">{error}</div>}
      {page === 'payment' && <>
        {step === 'lookup' && <form className="card" onSubmit={lookup}><h2>{preferences.guest_checkout_allowed ? 'Find your bill' : 'Access your account'}</h2><p className="card-copy">{preferences.guest_checkout_allowed ? 'No sign-in required. Enter the account number shown on your bill.' : 'Enter your account number to continue to the secure payment experience.'}</p><label>Account number<input name="account" data-testid="account-input" required defaultValue="4421" autoComplete="off" /></label><button data-testid="lookup-submit" disabled={busy}>{busy ? 'Finding Bill…' : 'Continue'}</button></form>}
        {step === 'method' && invoice && <section className="card"><Bill invoice={invoice}/><h2>Choose how to pay</h2><div className="choices">{acceptedMethods.includes('card') && <button data-testid="method-card" className={method === 'card' ? 'selected' : 'option'} onClick={() => setMethod('card')}>Card <small>{feeText('card', feeHandling)}</small></button>}{acceptedMethods.includes('ach') && <button data-testid="method-ach" className={method === 'ach' ? 'selected' : 'option'} onClick={() => setMethod('ach')}>Bank Account <small>{feeText('ach', feeHandling)}</small></button>}</div>{acceptedMethods.filter(value => value !== 'card' && value !== 'ach').length > 0 && <div className="method-chips">Also accepts {acceptedMethods.filter(value => value !== 'card' && value !== 'ach').map(humanize).join(' · ')}</div>}
          {(preferences.offer_autopay || preferences.offer_paperless) && <fieldset><legend>Optional preferences</legend>{preferences.offer_autopay && preferences.enroll_during_payment && <label className="check"><input type="checkbox" checked={autoPay} onChange={event => setAutoPay(event.target.checked)}/><span><strong>Enroll in AutoPay</strong><small>Use this method for future bills. Cancel anytime.</small></span></label>}{preferences.offer_paperless && <label className="check"><input type="checkbox" checked={paperless} onChange={event => setPaperless(event.target.checked)}/><span><strong>Switch to Paperless Billing</strong><small>Receive bills electronically instead of by mail.</small></span></label>}{(autoPay || paperless) && <><label>Name<input value={payerName} onChange={event => setPayerName(event.target.value)} required/></label><label>Email<input type="email" value={payerEmail} onChange={event => setPayerEmail(event.target.value)} required/></label></>}</fieldset>}
          <button data-testid="review-submit" onClick={() => setStep('review')} disabled={acceptedMethods.length === 0 || ((autoPay || paperless) && (!payerName || !payerEmail))}>Review Payment</button></section>}
        {step === 'review' && invoice && <section className="card"><h2>Review and confirm</h2><dl><div><dt>Bill amount</dt><dd>{money(invoice.amountCents)}</dd></div><div><dt>Service fee</dt><dd>{money(fee)}</dd></div><div className="total"><dt>Total</dt><dd>{money(invoice.amountCents + fee)}</dd></div></dl>{schedulesPayment && <div className="notice">This payment will be scheduled for {new Date(invoice.dueDate).toLocaleDateString()}.</div>}{(autoPay || paperless) && <div className="notice">You chose: {[autoPay && 'AutoPay', paperless && 'Paperless Billing'].filter(Boolean).join(' and ')}.</div>}<p className="consent">Selecting “{primaryAction?.label ?? 'Pay Now'}” authorizes this {schedulesPayment ? 'scheduled' : 'one-time'} payment. Optional preferences are recorded separately.</p><div className="actions"><button className="back" onClick={() => setStep('method')}>Back</button><button data-testid="pay-submit" disabled={busy} onClick={pay}>{busy ? 'Processing…' : (primaryAction?.label ?? `Pay ${money(invoice.amountCents + fee)}`)}</button></div></section>}
        {step === 'complete' && receipt && <section className="card success" data-testid="payment-confirmation" aria-live="polite"><div className="success-icon">✓</div><h2>{schedulesPayment ? 'Payment scheduled' : 'Payment received'}</h2><p>Confirmation <strong data-testid="confirmation-code">{receipt.confirmation}</strong></p><p>{money(receipt.amountCents + receipt.feeCents)} {schedulesPayment ? 'scheduled' : 'paid'} using the configured provider.</p>{autoPay && <span className="pill">AutoPay requested</span>}{paperless && <span className="pill">Paperless requested</span>}</section>}
      </>}
      {page === 'history' && <section className="card"><h2>Recent account activity</h2>{invoice ? <>{invoices.map(item => <Bill key={item.id} invoice={item}/>)}{payments.map(payment => <div className="history-row" key={payment.payment_id}><span>Payment {payment.confirmation}<small>{new Date(payment.created_at).toLocaleDateString()}</small></span><strong>{money(payment.total_cents)}</strong></div>)}</> : <><p className="card-copy">Find your bill first to load account-specific activity.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
      {page === 'preferences' && <section className="card"><h2>Communication preferences</h2>{payer ? <><label className="check"><input type="checkbox" checked={autoPay} onChange={event => setAutoPay(event.target.checked)}/><span><strong>AutoPay</strong><small>Automatically pay future bills.</small></span></label><label className="check"><input type="checkbox" checked={paperless} onChange={event => setPaperless(event.target.checked)}/><span><strong>Paperless Billing</strong><small>Receive bills electronically.</small></span></label><div className="preference-summary"><span>Payment reminders<strong>{payer.preferences.channels.map(humanize).join(', ') || reminderLabel(preferences.reminder_channel)}</strong></span></div><button disabled={busy} onClick={savePreferences}>{busy ? 'Saving…' : 'Save Preferences'}</button></> : <><p className="card-copy">Find your bill and register during checkout to manage account preferences.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
    </main>
    <Footer brand={config.brand} content={config.content} />
  </div>;
}

function Bill({ invoice }: { invoice: Invoice }) { return <div className="bill"><div><span>{invoice.description}</span><small>Due {new Date(invoice.dueDate).toLocaleDateString()}</small></div><strong>{money(invoice.amountCents)}</strong></div>; }
function experiencePreferences(config?: ExperienceDefinition): ExperiencePreferences { return config?.preferences ?? { guest_checkout_allowed: true, offer_autopay: true, enroll_during_payment: true, offer_paperless: true, reminder_channel: 'both', accepted_methods: config?.enabled_payment_capabilities ?? ['card', 'ach'], self_service_history: true, self_service_updates: true, fee_handling: 'mixed', preview: { default_device: 'desktop', enabled_scenarios: ['payment', 'history', 'communication', 'complex'] } }; }
function isAbsorbed(value: number | string) { return value === 0 || value === 'absorb'; }
function feeText(method: 'card'|'ach', handling: number | string) { return isAbsorbed(handling) ? 'No payer fee' : method === 'card' ? '$2.50 fee' : '$0.95 fee'; }
function reminderLabel(value: number | string) { return typeof value === 'number' ? ['Email', 'Text (SMS)', 'Both', 'None'][value] : humanize(value); }
function humanize(value: string) { return value.replaceAll('_', ' ').replace(/\b\w/g, match => match.toUpperCase()); }
function money(cents: number) { return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100); }
function toMessage(error: unknown) { return error instanceof Error ? error.message : 'The request failed.'; }
function billerSlug() { const match = window.location.pathname.match(/^\/pay\/([^/]+)/); return match?.[1] ?? import.meta.env.VITE_BILLER_SLUG ?? 'demo'; }
