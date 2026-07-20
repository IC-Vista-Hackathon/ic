import { FormEvent, useEffect, useMemo, useRef, useState } from 'react';
import { billerSlug, configEndpoint, previewBillerId } from './billerSlug';
import { settleInvoices, type BatchOutcome } from './batch';
import { NoOpenBillError } from './errors';
import { randomId } from './id';
import { trackEvent } from './insights';
import { ServicePaymentExperienceProvider, type PaymentQuote, type AssistantRecommendation, type AssistantMessage, type AssistantAction } from './provider';
import { BatchReview, Cart, Footer, Header, InvoiceSelectList, Intro, PaymentPlanChooser } from './skin';
import type { BatchReviewLine, CartSummary, InstallmentOption, PaymentPlanMode, SelectableInvoice } from './skin';
import { logError, logEvent, observed } from './telemetry';
import { categorizeError } from './telemetryPolicy';
import { errorMessage, fetchWithTimeout, requestError } from './http';
import type { ExperienceDefinition, ExperiencePreferences, Invoice, PayerProfile, PaymentHistory, PaymentMethod, PaymentReceipt, PaymentRequest } from './types';

type Step = 'lookup' | 'method' | 'review' | 'complete';
type Page = 'payment' | 'history' | 'preferences';
const PAYMENT_METHODS: PaymentMethod[] = ['card', 'ach'];
// Payer assistant is opt-in. Off by default so the payer page renders exactly as it does today;
// the local demo enables it with VITE_PAYER_ASSISTANT=true.
const ASSISTANT_ENABLED = import.meta.env.VITE_PAYER_ASSISTANT === 'true';

export function App() {
  const [config, setConfig] = useState<ExperienceDefinition>();
  const [configState, setConfigState] = useState<'loading'|'ready'|'error'>('loading');
  const [configAttempt, setConfigAttempt] = useState(0);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [payer, setPayer] = useState<PayerProfile>();
  const [payments, setPayments] = useState<PaymentHistory[]>([]);
  const [step, setStep] = useState<Step>('lookup');
  const [page, setPage] = useState<Page>('payment');
  const [method, setMethod] = useState<PaymentMethod>('card');
  // F4 amount-entry / installment-plan journey (single-invoice only; the default is 'full').
  const [planMode, setPlanMode] = useState<PaymentPlanMode>('full');
  const [amountInput, setAmountInput] = useState('');
  const [installmentCount, setInstallmentCount] = useState<number>();
  // Server-priced quote for the requested partial amount, tagged with the amount AND method it
  // priced so a stale quote is never shown while the payer is still typing or after switching method.
  const [planQuote, setPlanQuote] = useState<{ amountCents: number; method: PaymentMethod; quote: PaymentQuote }>();
  const [autoPay, setAutoPay] = useState(false);
  const [paperless, setPaperless] = useState(false);
  const [receipt, setReceipt] = useState<PaymentReceipt>();
  const [outcomes, setOutcomes] = useState<BatchOutcome[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [payerName, setPayerName] = useState('');
  const [payerEmail, setPayerEmail] = useState('');
  // Quotes are keyed `${invoiceId}::${method}` — every selected invoice is quoted per method.
  const [quotes, setQuotes] = useState<Record<string, PaymentQuote>>({});
  // Quote errors are keyed `${invoiceId}::${method}` so a failure is scoped to the bill that
  // failed; the per-method banner is derived from only the currently-selected invoices, so
  // deselecting the offending bill clears the error without a manual retry.
  const [quoteErrors, setQuoteErrors] = useState<Record<string, string>>({});
  const [quoteAttempt, setQuoteAttempt] = useState(0);
  const [recommendation, setRecommendation] = useState<AssistantRecommendation>();
  const [assistantState, setAssistantState] = useState<'idle'|'thinking'|'ready'|'error'>('idle');
  const [assistantAttempt, setAssistantAttempt] = useState(0);
  const [chat, setChat] = useState<AssistantMessage[]>([]);
  const [chatInput, setChatInput] = useState('');
  const [chatBusy, setChatBusy] = useState(false);
  const [chatAction, setChatAction] = useState<AssistantAction>();
  const chatEndRef = useRef<HTMLDivElement>(null);
  const paymentInFlight = useRef(false);
  // One stable Idempotency-Key per invoice; reused across retries so a partially-failed
  // batch never double-charges an invoice that already settled.
  const paymentKeys = useRef<Map<string, string>>(new Map());
  // Invoice::method pairs already quoted or in flight, so toggling the selection only fetches
  // newly-added pairs and never re-requests (or blanks) quotes already cached for other invoices.
  const requestedQuotes = useRef<Set<string>>(new Set());

  useEffect(() => {
    setConfigState('loading'); setError('');
    const slug = billerSlug(); const preview = previewBillerId(); const configUrl = import.meta.env.VITE_CONFIG_URL ?? configEndpoint(slug, preview);
    // Preview config is served from the draft (no published manifest); only wire the manifest for live slugs.
    const manifest = document.querySelector<HTMLLinkElement>('#experience-manifest'); if (manifest && !preview) manifest.href = `/api/public/experiences/${encodeURIComponent(slug)}/manifest.webmanifest`;
    observed('pwa.config.load', async () => { const response = await fetchWithTimeout(configUrl, { cache: 'no-store' }); if (!response.ok) throw await requestError(response, 'Experience configuration is unavailable.'); return validateConfig(await response.json()); })
      // Branding is evidence-gated (a biller may go live before any colors are chosen), so only
      // override the skin's default brand tokens when a value is actually set — an empty color
      // must fall back to the theme default, never blank out the button/link/bill styling.
      .then(value => { setConfig(value); setConfigState('ready'); document.title = value.pwa.name; if (value.brand.primary_color) document.documentElement.style.setProperty('--brand', value.brand.primary_color); if (value.brand.secondary_color) document.documentElement.style.setProperty('--brand-secondary', value.brand.secondary_color); if (value.brand.font_family) document.documentElement.style.setProperty('--brand-font', value.brand.font_family); })
      .catch(caught => { setConfigState('error'); setError(`Load payment experience: ${errorMessage(caught)}`); logError('pwa.config.failed', caught, { biller_slug: slug }); });
  }, [configAttempt]);

  const preferences = useMemo(() => experiencePreferences(config), [config]);
  const provider = useMemo(() => config ? new ServicePaymentExperienceProvider(config.biller_id) : undefined, [config]);
  const acceptedMethods = useMemo<PaymentMethod[]>(
    () => preferences.accepted_methods
      .filter(isPaymentMethod)
      .filter(value => config?.enabled_payment_capabilities.includes(value)),
    [config?.enabled_payment_capabilities, preferences.accepted_methods]);

  // Open invoices are payable; a biller with more than one open invoice gets the
  // multi-select + cart + batch experience, a single open invoice the degenerate flow.
  const openInvoices = useMemo(() => invoices.filter(item => item.status !== 'paid'), [invoices]);
  const multi = openInvoices.length > 1;
  const selectedInvoices = useMemo(() => openInvoices.filter(item => selectedIds.includes(item.id)), [openInvoices, selectedIds]);
  const invoice = selectedInvoices[0] ?? openInvoices[0];

  // Server-side quotes: fees come from the Payment Service (same policy the charge applies),
  // never computed client-side. quote.totalCents === amount when the biller absorbs the fee.
  const quoteOf = (invoiceId: string, forMethod: PaymentMethod) => quotes[`${invoiceId}::${forMethod}`];
  const quote = invoice ? quoteOf(invoice.id, method) : undefined;
  useEffect(() => {
    if (!provider || selectedInvoices.length === 0) return;
    const methods = PAYMENT_METHODS.filter(value => acceptedMethods.includes(value));
    for (const item of selectedInvoices) {
      for (const value of methods) {
        const key = `${item.id}::${value}`;
        // Dedupe against pairs already quoted or in flight (tracked in requestedQuotes) so
        // toggling the selection only fetches newly-added pairs — it never re-requests or
        // blanks quotes already fetched for the other still-selected invoices.
        if (requestedQuotes.current.has(key)) continue;
        requestedQuotes.current.add(key);
        provider.quote(item.id, value)
          .then(result => {
            setQuotes(previous => ({ ...previous, [key]: result }));
            setQuoteErrors(previous => { if (!previous[key]) return previous; const next = { ...previous }; delete next[key]; return next; });
          })
          .catch(caught => {
            logError('pwa.payment.quote_failed', caught, { method: value });
            requestedQuotes.current.delete(key); // allow a retry to re-request this pair
            setQuoteErrors(previous => ({ ...previous, [key]: errorMessage(caught) }));
          });
      }
    }
  }, [acceptedMethods, provider, selectedInvoices, quoteAttempt]);

  // Payer-side agent turn (opt-in, single-invoice only): once a bill is in hand, ask the assistant
  // to read it and recommend a method + timing. Advisory only — it never moves money and the payer
  // stays in control below.
  useEffect(() => {
    if (!ASSISTANT_ENABLED || !provider || !invoice || multi) { setRecommendation(undefined); setAssistantState('idle'); setChat([]); setChatAction(undefined); return; }
    let cancelled = false;
    setAssistantState('thinking'); setRecommendation(undefined); setChat([]); setChatAction(undefined);
    provider.askAssistant(invoice.id, invoice.accountNumber)
      .then(result => { if (!cancelled) { setRecommendation(result); setAssistantState('ready'); setChat([{ role: 'assistant', content: result.reply }]); trackEvent('pwa.assistant_recommended', { method: isPaymentMethod(result.method) ? result.method : 'card', scheduled: !!result.scheduledFor }); } })
      .catch(caught => { if (!cancelled) { setAssistantState('error'); logError('pwa.assistant.failed', caught); } });
    return () => { cancelled = true; };
  }, [provider, invoice, multi, assistantAttempt]);
  useEffect(() => { chatEndRef.current?.scrollIntoView?.({ block: 'nearest' }); }, [chat, chatBusy]);
  async function sendChat(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const question = chatInput.trim();
    if (!question || !provider || !invoice || chatBusy) return;
    const history: AssistantMessage[] = [...chat, { role: 'user', content: question }];
    setChat(history); setChatInput(''); setChatBusy(true); setChatAction(undefined);
    trackEvent('pwa.assistant_asked', {});
    try {
      const result = await provider.chat(invoice.id, invoice.accountNumber, history);
      setChat([...history, { role: 'assistant', content: result.reply }]);
      // Surface the in-chat confirm control only when the assistant offered one and its method
      // is actually accepted here. The payer's tap on it is still the explicit confirmation.
      setChatAction(result.action && isPaymentMethod(result.action.method) && acceptedMethods.includes(result.action.method) ? result.action : undefined);
    } catch (caught) {
      logError('pwa.assistant.failed', caught);
      setChat([...history, { role: 'assistant', content: 'Sorry — I couldn’t answer that just now. You can still choose a method below.' }]);
    } finally {
      setChatBusy(false);
    }
  }
  // The chat's "Confirm & pay" control: the payer's explicit confirmation. It selects the offered
  // method and submits through the same guarded single-invoice pay() path, then lands on the receipt.
  // The confirm button advertises the assistant's full-balance total, so it always pays in full —
  // it never inherits a partial/installment plan the payer may have selected below.
  async function confirmFromChat(action: AssistantAction) {
    if (!isPaymentMethod(action.method)) return;
    selectMethod(action.method);
    trackEvent('pwa.assistant_pay_confirmed', { method: action.method });
    setChatAction(undefined);
    await paySingle(action.method, true);
  }
  function applyRecommendation() { if (recommendation && isPaymentMethod(recommendation.method) && acceptedMethods.includes(recommendation.method)) selectMethod(recommendation.method); }

  // Aggregate money for the current method over the selected invoices. All sums are computed
  // here in the core from server quotes; the authorable cart/review only render the labels.
  const selectedQuotes = selectedInvoices.map(item => quoteOf(item.id, method));
  const allQuoted = selectedInvoices.length > 0 && selectedQuotes.every(entry => entry !== undefined);
  const cartAmountCents = selectedInvoices.reduce((sum, item) => sum + item.amountCents, 0);
  // The payer-facing fee is total − amount: when the biller absorbs the fee the quote reports a
  // non-zero feeCents but total === amount, so the payer is charged nothing extra. Mirror the
  // single-invoice quoteFeeText so the cart/review never show a fee the payer will not pay.
  const cartFeeCents = allQuoted ? selectedInvoices.reduce((sum, item) => sum + payerFeeCents(quoteOf(item.id, method)!, item.amountCents), 0) : undefined;
  const cartTotalCents = allQuoted ? selectedQuotes.reduce((sum, entry) => sum + (entry?.totalCents ?? 0), 0) : undefined;

  const primaryAction = config?.ui?.actions.find(action => action.id === 'primary-payment-action');
  const schedulesPayment = primaryAction?.action === 1 || primaryAction?.action === 'schedule_payment';

  // F4 policy: a biller whose configuration allows installments unlocks the partial-amount /
  // installment-plan journey. Eligibility comes straight from the presented billing policy — the
  // server independently re-validates every amount and plan, so this only gates what we *show*.
  // The alternate journey is offered only on the single-invoice path (F3's cart owns multi-bill).
  const installmentMax = useMemo(() => {
    const modes = config?.billing?.categories ?? [];
    const eligible = modes.filter(category => category.payment_mode === 'installments_allowed' || category.payment_mode === 1);
    return eligible.reduce((max, category) => Math.max(max, category.maximum_installments ?? 0), 0);
  }, [config?.billing?.categories]);
  const planEligible = !multi && installmentMax > 0;
  const allowPartial = planEligible;
  const allowInstallments = planEligible && installmentMax >= 2;
  const outstandingCents = invoice?.amountCents ?? 0;

  const partialAmountCents = useMemo(() => parseAmountCents(amountInput), [amountInput]);
  const amountError = useMemo(() => {
    if (planMode !== 'partial' || amountInput.trim() === '') return undefined;
    if (partialAmountCents === undefined) return 'Enter a valid dollar amount, e.g. 25.00.';
    if (partialAmountCents <= 0) return 'Enter an amount greater than zero.';
    if (partialAmountCents > outstandingCents) return `Amount can’t exceed the ${money(outstandingCents)} balance.`;
    return undefined;
  }, [planMode, amountInput, partialAmountCents, outstandingCents]);
  const partialReady = partialAmountCents !== undefined && partialAmountCents > 0 && partialAmountCents <= outstandingCents;

  const installmentOptions = useMemo<InstallmentOption[]>(() => {
    if (!allowInstallments) return [];
    return [2, 3, 4, 6, 12]
      .filter(count => count <= installmentMax)
      .map(count => ({ count, label: `${count} monthly payments of about ${money(Math.ceil(outstandingCents / count))}` }));
  }, [allowInstallments, installmentMax, outstandingCents]);

  // The quote that prices the current single-invoice journey: the partial quote when a valid
  // partial amount is entered, otherwise the full-balance quote (installments settle the full
  // balance too, so the full quote represents the plan total).
  const partialQuote = planQuote && planQuote.amountCents === partialAmountCents && planQuote.method === method ? planQuote.quote : undefined;
  const activeQuote = !multi && planMode === 'partial' ? partialQuote : quote;
  const planReady = planMode === 'full' ? true : planMode === 'partial' ? partialReady : installmentCount !== undefined;
  const planQuoteReady = planMode === 'partial' ? partialQuote !== undefined : planMode === 'installment' ? quote !== undefined : quote !== undefined;

  // Price a valid partial amount server-side (never client-side) so the review total matches the
  // charge. Skipped for full/installment, which the full-balance quote already covers.
  useEffect(() => {
    if (multi || !provider || !invoice || planMode !== 'partial' || !partialReady || partialAmountCents === undefined) return;
    if (planQuote && planQuote.amountCents === partialAmountCents && planQuote.method === method) return;
    let active = true;
    provider.quote(invoice.id, method, partialAmountCents)
      .then(result => { if (active) setPlanQuote({ amountCents: partialAmountCents, method, quote: result }); })
      .catch(caught => logError('pwa.payment.quote_failed', caught, { method }));
    return () => { active = false; };
  }, [multi, provider, invoice, planMode, partialReady, partialAmountCents, method, planQuote]);

  useEffect(() => { if (!acceptedMethods.includes(method)) setMethod(acceptedMethods.includes('ach') ? 'ach' : 'card'); }, [acceptedMethods, method]);

  async function lookup(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (!provider) return; setBusy(true); setError(''); paymentKeys.current = new Map(); requestedQuotes.current = new Set(); setQuotes({}); setQuoteErrors({}); setOutcomes([]); setReceipt(undefined); setPlanMode('full'); setAmountInput(''); setInstallmentCount(undefined); setPlanQuote(undefined); try { const data = new FormData(event.currentTarget); const account = String(data.get('account')); const loaded = await provider.getInvoices(account); const open = loaded.filter(item => item.status !== 'paid'); if (open.length === 0) throw new NoOpenBillError(); setInvoices(loaded); setSelectedIds(open.map(item => item.id)); const profile = await provider.findPayer(account); setPayer(profile); if (profile) { setAutoPay(profile.preferences.autopay); setPaperless(profile.preferences.paperless); setPayerName(profile.name); setPayerEmail(profile.email); setPayments(await provider.getPayments(profile.payer_id)); } setStep('method'); trackEvent('pwa.bill_lookup', { outcome: 'found' }); } catch (caught) { setError(`Find bill: ${errorMessage(caught)}`); logError('pwa.invoice.lookup_failed', caught); if (caught instanceof NoOpenBillError) trackEvent('pwa.bill_lookup', { outcome: 'no_open_bill' }); else trackEvent('pwa.bill_lookup', { outcome: 'failed', error_category: categorizeError(caught) }); } finally { setBusy(false); } }

  // Re-request only the pairs that are still missing (failed pairs were dropped from the set);
  // cached quotes are kept, so a retry never blanks quotes already fetched for other invoices.
  function retryQuotes() { setQuoteErrors({}); setQuoteAttempt(value => value + 1); }
  function toggleInvoice(invoiceId: string) { setSelectedIds(previous => previous.includes(invoiceId) ? previous.filter(id => id !== invoiceId) : [...previous, invoiceId]); }
  function selectAllInvoices() { setSelectedIds(openInvoices.map(item => item.id)); }
  function clearSelectedInvoices() { setSelectedIds([]); }

  function keyFor(invoiceId: string) { let key = paymentKeys.current.get(invoiceId); if (!key) { key = randomId(); paymentKeys.current.set(invoiceId, key); } return key; }
  function paymentRequestFor(item: Invoice, methodOverride?: PaymentMethod): PaymentRequest {
    return { invoiceId: item.id, method: methodOverride ?? method, autoPay, paperless, scheduledFor: schedulesPayment ? item.dueDate : undefined, payerName, payerEmail, accountNumber: item.accountNumber, idempotencyKey: keyFor(item.id) };
  }
  // Build the single-invoice request for the chosen journey. Partial/installment requests carry the
  // requested amount / plan and are never pre-scheduled (the server dates the installment schedule);
  // the full path is byte-for-byte the existing request. Amounts are only *echoed* — the server
  // re-validates them against the balance it looks up.
  function planRequestFor(item: Invoice, methodOverride?: PaymentMethod): PaymentRequest {
    const base = paymentRequestFor(item, methodOverride);
    if (planMode === 'partial') return { ...base, scheduledFor: undefined, amountCents: partialAmountCents };
    if (planMode === 'installment') return { ...base, scheduledFor: undefined, installmentCount };
    return base;
  }
  // Changing the amount or plan changes the charge parameters, so the idempotency key is reset —
  // the server also rejects reusing a key with different amount/plan parameters.
  function selectPlanMode(next: PaymentPlanMode) { if (next === planMode) return; paymentKeys.current.clear(); setPlanMode(next); trackEvent('pwa.plan_mode_selected', { mode: next }); }
  function changeAmount(value: string) { paymentKeys.current.clear(); setAmountInput(value); }
  function selectInstallmentCount(count: number) { paymentKeys.current.clear(); setInstallmentCount(count); trackEvent('pwa.installment_count_selected', { count }); }

  async function refreshHistory(accountNumber: string) {
    try { const profile = await provider!.findPayer(accountNumber); setPayer(profile); if (profile) setPayments(await provider!.getPayments(profile.payer_id)); }
    catch (caught) { logError('pwa.payment.history_refresh_failed', caught, { method }); }
  }

  async function pay() { return multi ? payBatch() : paySingle(); }

  async function paySingle(methodOverride?: PaymentMethod, forceFullPayment = false) {
    if (!invoice || !provider || paymentInFlight.current) return;
    const payMethod = methodOverride ?? method;
    paymentInFlight.current = true;
    setBusy(true);
    setError('');
    trackEvent('pwa.payment_submitted', { method: payMethod, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
    try {
      const completed = await provider.pay(forceFullPayment ? paymentRequestFor(invoice, payMethod) : planRequestFor(invoice, payMethod));
      paymentKeys.current.delete(invoice.id);
      setReceipt(completed);
      setStep('complete');
      if (completed.preferenceUpdateFailed) {
        setError('Payment completed, but optional preferences could not be saved. You can retry them from Preferences.');
      }
      logEvent('pwa.payment.completed', { method: payMethod, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
      trackEvent('pwa.payment_completed', { method: payMethod, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
      if (completed.payerAccountId) await refreshHistory(invoice.accountNumber);
    } catch (caught) {
      setError(`Submit payment: ${errorMessage(caught)}`);
      logError('pwa.payment.failed', caught, { method: payMethod, scheduled: schedulesPayment });
      trackEvent('pwa.payment_failed', { method: payMethod, scheduled: schedulesPayment, error_category: categorizeError(caught) });
    } finally {
      paymentInFlight.current = false;
      setBusy(false);
    }
  }

  // Batch checkout: settle EACH selected invoice with its own Idempotency-Key by looping the
  // existing single-invoice, exactly-once POST /payments. On a partial failure only the
  // still-unpaid invoices are retried (same keys), so a succeeded charge is never repeated.
  async function payBatch() {
    if (!provider || paymentInFlight.current) return;
    const paidIds = new Set(outcomes.filter(entry => entry.receipt).map(entry => entry.invoiceId));
    const pending = selectedInvoices.filter(item => !paidIds.has(item.id));
    if (pending.length === 0) return;
    paymentInFlight.current = true;
    setBusy(true);
    setError('');
    trackEvent('pwa.payment_submitted', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
    try {
      const requests = pending.map(item => paymentRequestFor(item));
      const result = await settleInvoices(request => provider.pay(request), requests);
      const merged = mergeOutcomes(outcomes, result.outcomes);
      setOutcomes(merged);
      for (const entry of result.outcomes) if (entry.receipt) paymentKeys.current.delete(entry.invoiceId);
      setStep('complete');
      if (result.allSucceeded) {
        logEvent('pwa.payment.completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
        trackEvent('pwa.payment_completed', { method, scheduled: schedulesPayment, autopay_opt_in: autoPay, paperless_opt_in: paperless });
        const settled = merged.find(entry => entry.receipt);
        if (settled?.receipt?.payerAccountId) await refreshHistory(pending[0].accountNumber);
        if (merged.some(entry => entry.receipt?.preferenceUpdateFailed)) {
          setError('Payment completed, but optional preferences could not be saved. You can retry them from Preferences.');
        }
      } else {
        const failed = merged.filter(entry => entry.error).length;
        setError(`${failed} of ${merged.length} payment${merged.length === 1 ? '' : 's'} could not be completed. Paid invoices were charged once; you can retry the rest.`);
        logError('pwa.payment.batch_partial', new Error('batch partial failure'), { method });
        trackEvent('pwa.payment_failed', { method, scheduled: schedulesPayment, error_category: 'unknown' });
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
  // Changing method changes the charge parameters, so the per-invoice idempotency keys are
  // reset — a fresh attempt under the new method must not reuse a prior method's key.
  function selectMethod(next: PaymentMethod) { if (next !== method) trackEvent('pwa.payment_method_selected', { method: next }); paymentKeys.current.clear(); setMethod(next); }
  function openReview() { trackEvent('pwa.review_opened', { method, scheduled: schedulesPayment }); setStep('review'); }
  function changeAutoPay(enabled: boolean) { setAutoPay(enabled); trackEvent('pwa.autopay_changed', { enabled }); }
  function changePaperless(enabled: boolean) { setPaperless(enabled); trackEvent('pwa.paperless_changed', { enabled }); }

  // Preformatted view models handed to the authorable (presentational) flow components.
  // The core owns every money value and selection decision; the skin only renders + calls back.
  const selectable: SelectableInvoice[] = openInvoices.map(item => ({
    id: item.id, typeLabel: item.type, description: item.description,
    dueDateLabel: new Date(item.dueDate).toLocaleDateString(), amountLabel: money(item.amountCents),
    statusColor: item.statusColor, statusLabel: item.statusColor ? statusLabel(item.statusColor) : undefined,
    note: item.note, noteEmphasis: item.noteEmphasis, selected: selectedIds.includes(item.id),
  }));
  const cartSummary: CartSummary = {
    lines: selectedInvoices.map(item => ({ id: item.id, label: item.description, typeLabel: item.type, amountLabel: money(item.amountCents) })),
    count: selectedInvoices.length,
    subtotalLabel: money(cartAmountCents),
    feeLabel: cartFeeCents === undefined ? undefined : cartFeeCents === 0 ? 'No payer fee' : money(cartFeeCents),
    totalLabel: cartTotalCents === undefined ? money(cartAmountCents) : money(cartTotalCents),
  };
  const batchLines: BatchReviewLine[] = selectedInvoices.map(item => {
    const entryQuote = quoteOf(item.id, method);
    const entryFeeCents = entryQuote ? payerFeeCents(entryQuote, item.amountCents) : undefined;
    const outcome = outcomes.find(entry => entry.invoiceId === item.id);
    const status = outcome?.receipt ? 'paid' : outcome?.error ? 'failed' : 'pending';
    return {
      id: item.id, label: item.description, typeLabel: item.type,
      amountLabel: money(item.amountCents),
      feeLabel: entryFeeCents === undefined ? '…' : entryFeeCents === 0 ? 'No payer fee' : `${money(entryFeeCents)} fee`,
      totalLabel: outcome?.receipt ? money(outcome.receipt.totalCents) : entryQuote ? money(entryQuote.totalCents) : '…',
      status, statusMessage: outcome?.receipt ? outcome.receipt.confirmation : outcome?.error,
    };
  });
  // Surface a quote error only if a currently-selected invoice failed for the active method;
  // a stale error for a deselected bill is ignored.
  const methodQuoteError = selectedInvoices.map(item => quoteErrors[`${item.id}::${method}`]).find(Boolean);
  const methodFeeText = (forMethod: PaymentMethod) => {
    const entries = selectedInvoices.map(item => ({ item, quote: quoteOf(item.id, forMethod) }));
    if (entries.length === 0 || !entries.every(entry => entry.quote !== undefined)) return '…';
    const fee = entries.reduce((sum, entry) => sum + payerFeeCents(entry.quote!, entry.item.amountCents), 0);
    return fee === 0 ? 'No payer fee' : `${money(fee)} fee`;
  };
  const allSelectedPaid = selectedInvoices.length > 0 && selectedInvoices.every(item => outcomes.find(entry => entry.invoiceId === item.id)?.receipt);
  const paidTotalCents = outcomes.reduce((sum, entry) => sum + (entry.receipt?.totalCents ?? 0), 0);

  // Single-invoice review labels for the chosen journey. Every amount is server-priced (activeQuote)
  // or the authoritative balance; installments are priced per-installment by the server on enrollment.
  const chargeAmountCents = planMode === 'partial' ? (partialAmountCents ?? 0) : outstandingCents;
  const reviewFeeText = planMode === 'installment' ? 'Calculated per installment' : quoteFeeText(activeQuote, chargeAmountCents);
  const reviewTotalLabel = planMode === 'installment' ? money(outstandingCents) : activeQuote ? money(activeQuote.totalCents) : '…';
  const payReady = planMode === 'installment' ? planReady : activeQuote !== undefined;
  const payButtonLabel = planMode === 'installment' && installmentCount ? `Start ${installmentCount}-payment plan`
    : planMode === 'partial' && activeQuote ? `Pay ${money(activeQuote.totalCents)}`
    : (primaryAction?.label ?? (activeQuote ? `Pay ${money(activeQuote.totalCents)}` : 'Quote unavailable'));

  if (!config) return <main className="center"><div className="card config-state" aria-live="polite"><h1>{configState === 'error' ? 'Payment experience unavailable' : 'Loading payment experience'}</h1><p>{error || 'Loading your secure, branded payment page…'}</p>{configState === 'error' && <button onClick={() => setConfigAttempt(value => value + 1)}>Retry</button>}</div></main>;
  return <div className="app">
    <Header brand={config.brand} />
    <nav className="account-nav" aria-label="Account services"><button className={page === 'payment' ? 'active' : ''} onClick={() => navigate('payment')}>Pay Bill</button>{preferences.self_service_history && <button className={page === 'history' ? 'active' : ''} onClick={() => navigate('history')}>Account History</button>}{preferences.self_service_updates && <button className={page === 'preferences' ? 'active' : ''} onClick={() => navigate('preferences')}>Preferences</button>}</nav>
    <main><Intro eyebrow="Account services" heading={page === 'payment' ? config.content.heading : page === 'history' ? 'Account history' : 'Communication preferences'} subheading={page === 'payment' ? config.content.introduction : `Manage services for ${config.brand.display_name}.`} />{error && <div className="alert" role="alert" data-testid="error">{error}</div>}
      {page === 'payment' && <>
        {config.billing?.categories.length ? <section className="card" aria-label="Billing options"><h2>Billing options</h2>{config.billing.categories.map(category => <div className="history-row" key={category.id}><span><strong>{category.display_name}</strong><small>{category.cadence_label} · {category.state_summary}</small></span><strong>{paymentTermsLabel(category.payment_mode, category.maximum_installments)}</strong></div>)}</section> : null}
        {step === 'lookup' && <form className="card" onSubmit={lookup}><h2>{preferences.guest_checkout_allowed ? 'Find your bill' : 'Access your account'}</h2><p className="card-copy">{preferences.guest_checkout_allowed ? 'No sign-in required. Enter the account number shown on your bill.' : 'Enter your account number to continue to the secure payment experience.'}</p><label>Account number<input name="account" data-testid="account-input" required defaultValue="4421" autoComplete="off" /></label><button data-testid="lookup-submit" disabled={busy}>{busy ? 'Finding Bill…' : 'Continue'}</button></form>}
        {step === 'method' && invoice && <>
          {ASSISTANT_ENABLED && !multi && assistantState !== 'idle' && <section className="card assistant" data-testid="assistant" aria-label="Payment assistant">
            <div className="assistant-head"><span className="assistant-avatar" aria-hidden="true">✦</span><div><strong>Payment assistant</strong><small>Reviews your bill and suggests a way to pay. You decide.</small></div></div>
            {assistantState === 'thinking' && <p className="assistant-reply" aria-live="polite">Reviewing your bill and comparing payment options…</p>}
            {assistantState === 'error' && <div className="alert" role="alert">The assistant is unavailable right now — you can still choose a method below. <button type="button" onClick={() => setAssistantAttempt(value => value + 1)}>Retry</button></div>}
            {assistantState === 'ready' && recommendation && <>
              <div className="assistant-transcript" data-testid="assistant-transcript" aria-live="polite">
                {chat.map((message, index) => <p key={index} className={`assistant-bubble ${message.role}`} data-testid={`assistant-${message.role}`}>{message.content}</p>)}
                {chatBusy && <p className="assistant-bubble assistant" data-testid="assistant-typing">Thinking…</p>}
                <div ref={chatEndRef} />
              </div>
              {chatAction && <button type="button" className="assistant-confirm" data-testid="assistant-confirm" disabled={busy} onClick={() => confirmFromChat(chatAction)}>{busy ? 'Submitting…' : `Confirm & pay ${money(chatAction.totalCents)} with ${methodLabel(chatAction.method)}`}</button>}
              <div className="assistant-plan"><span>Recommended</span><strong>{methodLabel(recommendation.method)} · {money(recommendation.totalCents)}{recommendation.scheduledFor ? ` · scheduled ${new Date(recommendation.scheduledFor).toLocaleDateString()}` : ' · pay now'}</strong></div>
              {isPaymentMethod(recommendation.method) && acceptedMethods.includes(recommendation.method) && method !== recommendation.method && <button type="button" data-testid="assistant-apply" onClick={applyRecommendation}>Use {methodLabel(recommendation.method)}</button>}
              <form className="assistant-ask" onSubmit={sendChat}>
                <input data-testid="assistant-input" value={chatInput} onChange={event => setChatInput(event.target.value)} placeholder="Ask about fees, timing, or a method…" aria-label="Ask the payment assistant" disabled={chatBusy} />
                <button type="submit" data-testid="assistant-send" disabled={chatBusy || !chatInput.trim()}>Ask</button>
              </form></>}
          </section>}
          {multi && <InvoiceSelectList heading="Select the bills to pay" invoices={selectable} onToggle={toggleInvoice} onSelectAll={selectAllInvoices} onClearAll={clearSelectedInvoices} allSelected={openInvoices.length > 0 && selectedInvoices.length === openInvoices.length} />}
          <section className="card">{!multi && <Bill invoice={invoice}/>}<h2>Choose how to pay</h2><div className="choices">{acceptedMethods.includes('card') && <button data-testid="method-card" className={method === 'card' ? 'selected' : 'option'} onClick={() => selectMethod('card')}>Card <small>{multi ? methodFeeText('card') : quoteFeeText(quoteOf(invoice.id, 'card'), invoice.amountCents)}</small></button>}{acceptedMethods.includes('ach') && <button data-testid="method-ach" className={method === 'ach' ? 'selected' : 'option'} onClick={() => selectMethod('ach')}>Bank Account <small>{multi ? methodFeeText('ach') : quoteFeeText(quoteOf(invoice.id, 'ach'), invoice.amountCents)}</small></button>}</div>
          {methodQuoteError && <div className="alert" role="alert" data-testid="quote-error">We couldn’t prepare this payment method. {methodQuoteError} <button type="button" onClick={retryQuotes}>Retry quote</button></div>}
          {!multi && (allowPartial || allowInstallments) && <PaymentPlanChooser allowPartial={allowPartial} allowInstallments={allowInstallments} mode={planMode} onModeChange={selectPlanMode} fullLabel={`Pay the full balance — ${money(outstandingCents)}`} amountValue={amountInput} onAmountChange={changeAmount} amountHint={`Enter any amount up to ${money(outstandingCents)}.`} amountError={amountError} installmentOptions={installmentOptions} selectedInstallmentCount={installmentCount} onInstallmentCountChange={selectInstallmentCount} />}
          {(preferences.offer_autopay || preferences.offer_paperless) && <fieldset><legend>Optional preferences</legend>{preferences.offer_autopay && preferences.enroll_during_payment && <label className="check"><input type="checkbox" checked={autoPay} onChange={event => changeAutoPay(event.target.checked)}/><span><strong>Enroll in AutoPay</strong><small>Use this method for future bills. Cancel anytime.</small></span></label>}{preferences.offer_paperless && <label className="check"><input type="checkbox" checked={paperless} onChange={event => changePaperless(event.target.checked)}/><span><strong>Switch to Paperless Billing</strong><small>Receive bills electronically instead of by mail.</small></span></label>}{(autoPay || paperless) && <><label>Name<input value={payerName} onChange={event => setPayerName(event.target.value)} required/></label><label>Email<input type="email" value={payerEmail} onChange={event => setPayerEmail(event.target.value)} required/></label></>}</fieldset>}
          <button data-testid="review-submit" onClick={openReview} disabled={selectedInvoices.length === 0 || !allQuoted || !!methodQuoteError || (!multi && (!planReady || !planQuoteReady)) || ((autoPay || paperless) && (!payerName || !payerEmail))}>Review Payment</button></section>
          {multi && <Cart summary={cartSummary} onRemove={toggleInvoice} emptyText="Select at least one bill to continue." />}</>}
        {step === 'review' && invoice && (multi
          ? <section className="card"><BatchReview heading="Review and confirm" lines={batchLines} totalLabel={cartTotalCents !== undefined ? money(cartTotalCents) : '…'} consentText={`Selecting “${primaryAction?.label ?? 'Pay Now'}” authorizes ${selectedInvoices.length} ${schedulesPayment ? 'scheduled' : 'one-time'} payment${selectedInvoices.length === 1 ? '' : 's'}, one per invoice. Optional preferences are recorded separately.`} />{(autoPay || paperless) && <div className="notice">You chose: {[autoPay && 'AutoPay', paperless && 'Paperless Billing'].filter(Boolean).join(' and ')}.</div>}<div className="actions"><button className="back" onClick={() => setStep('method')}>Back</button><button data-testid="pay-submit" disabled={busy || !allQuoted} onClick={pay}>{busy ? 'Processing…' : (cartTotalCents !== undefined ? `Pay ${money(cartTotalCents)}` : 'Quote unavailable')}</button></div></section>
          : <section className="card"><h2>Review and confirm</h2><dl><div><dt>{planMode === 'full' ? 'Bill amount' : planMode === 'partial' ? 'Amount to pay' : 'Balance to finance'}</dt><dd data-testid="review-amount">{money(chargeAmountCents)}</dd></div>{planMode === 'installment' && installmentCount && <div><dt>Installments</dt><dd data-testid="review-installments">{installmentCount} monthly payments</dd></div>}<div><dt>Service fee</dt><dd>{reviewFeeText}</dd></div><div className="total"><dt>{planMode === 'installment' ? 'Plan total' : 'Total'}</dt><dd data-testid="review-total">{reviewTotalLabel}</dd></div></dl>{schedulesPayment && planMode === 'full' && <div className="notice">This payment will be scheduled for {new Date(invoice.dueDate).toLocaleDateString()}.</div>}{planMode === 'partial' && <div className="notice">This is a partial payment; the remaining balance stays due.</div>}{planMode === 'installment' && <div className="notice">You’ll enroll in a {installmentCount}-payment plan; each installment is scheduled and charged automatically.</div>}{(autoPay || paperless) && <div className="notice">You chose: {[autoPay && 'AutoPay', paperless && 'Paperless Billing'].filter(Boolean).join(' and ')}.</div>}<p className="consent">Selecting “{planMode === 'installment' ? 'Start plan' : (primaryAction?.label ?? 'Pay Now')}” authorizes this {planMode === 'installment' ? 'installment plan' : schedulesPayment && planMode === 'full' ? 'scheduled payment' : 'one-time payment'}. Optional preferences are recorded separately.</p><div className="actions"><button className="back" onClick={() => setStep('method')}>Back</button><button data-testid="pay-submit" disabled={busy || !payReady} onClick={pay}>{busy ? 'Processing…' : payButtonLabel}</button></div></section>)}
        {step === 'complete' && (multi
          ? <section className="card success" data-testid="batch-result" aria-live="polite">{allSelectedPaid ? <><div className="success-icon">✓</div><h2 data-testid="payment-confirmation">{schedulesPayment ? 'Payments scheduled' : 'Payments received'}</h2></> : <h2>Some payments need your attention</h2>}<BatchReview heading="Payment results" lines={batchLines} totalLabel={money(paidTotalCents)} consentText={allSelectedPaid ? `${money(paidTotalCents)} ${schedulesPayment ? 'scheduled' : 'paid'} across ${selectedInvoices.length} invoice${selectedInvoices.length === 1 ? '' : 's'} using the configured provider.` : 'Paid invoices were charged exactly once. Use Retry unpaid to complete the rest — settled invoices will not be charged again.'} />{!allSelectedPaid && <div className="actions"><button className="back" onClick={() => setStep('method')}>Back</button><button data-testid="retry-batch" disabled={busy} onClick={pay}>{busy ? 'Processing…' : 'Retry unpaid'}</button></div>}</section>
          : receipt && (receipt.installmentPlanId
            ? <section className="card success" data-testid="payment-confirmation" aria-live="polite"><div className="success-icon">✓</div><h2>Installment plan started</h2><p>Confirmation <strong data-testid="confirmation-code">{receipt.confirmation}</strong></p><p>{receipt.installmentCount} scheduled payments totaling {money(receipt.totalCents)} using the configured provider. Nothing is charged until each installment’s date.</p>{autoPay && <span className="pill">AutoPay requested</span>}{paperless && <span className="pill">Paperless requested</span>}</section>
            : <section className="card success" data-testid="payment-confirmation" aria-live="polite"><div className="success-icon">✓</div><h2>{schedulesPayment ? 'Payment scheduled' : 'Payment received'}</h2><p>Confirmation <strong data-testid="confirmation-code">{receipt.confirmation}</strong></p><p>{money(receipt.totalCents)} {schedulesPayment ? 'scheduled' : 'paid'} using the configured provider.</p>{autoPay && <span className="pill">AutoPay requested</span>}{paperless && <span className="pill">Paperless requested</span>}</section>))}
      </>}
      {page === 'history' && <section className="card"><h2>Recent account activity</h2>{invoice ? <>{invoices.map(item => <Bill key={item.id} invoice={item}/>)}{payments.map(payment => <div className="history-row" key={payment.payment_id}><span>Payment {payment.confirmation}<small>{new Date(payment.created_at).toLocaleDateString()}</small></span><strong>{money(payment.total_cents)}</strong></div>)}</> : <><p className="card-copy">Find your bill first to load account-specific activity.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
      {page === 'preferences' && <section className="card"><h2>Communication preferences</h2>{payer ? <><label className="check"><input type="checkbox" checked={autoPay} onChange={event => changeAutoPay(event.target.checked)}/><span><strong>AutoPay</strong><small>Automatically pay future bills.</small></span></label><label className="check"><input type="checkbox" checked={paperless} onChange={event => changePaperless(event.target.checked)}/><span><strong>Paperless Billing</strong><small>Receive bills electronically.</small></span></label><div className="preference-summary"><span>Payment reminders<strong>{payer.preferences.channels.map(humanize).join(', ') || reminderLabel(preferences.reminder_channel)}</strong></span></div><button disabled={busy} onClick={savePreferences}>{busy ? 'Saving…' : 'Save Preferences'}</button></> : <><p className="card-copy">Find your bill and register during checkout to manage account preferences.</p><button onClick={() => navigate('payment')}>Find My Bill</button></>}</section>}
    </main>
    <Footer brand={config.brand} content={config.content} />
  </div>;
}

function Bill({ invoice }: { invoice: Invoice }) {
  return <div className="bill">
    <div className="bill-main">
      <div className="bill-desc">
        {invoice.type && <span className="bill-type" data-testid="bill-type">{invoice.type}</span>}
        <span>{invoice.description}</span>
        <small>Due {new Date(invoice.dueDate).toLocaleDateString()}</small>
      </div>
      <div className="bill-right">
        {invoice.statusColor && <span className={`status-dot status-${invoice.statusColor}`} data-testid="status-dot" title={statusLabel(invoice.statusColor)} aria-label={statusLabel(invoice.statusColor)} />}
        <strong>{money(invoice.amountCents)}</strong>
      </div>
    </div>
    {invoice.note && <p className={`bill-note${invoice.noteEmphasis ? ' bill-note-strong' : ''}`} data-testid="bill-note">{invoice.note}</p>}
  </div>;
}
function statusLabel(color: string) { return color === 'yellow' ? 'Overdue — in grace period' : color === 'green' ? 'Not yet due' : color === 'red' ? 'Past due' : color; }
function experiencePreferences(config?: ExperienceDefinition): ExperiencePreferences { return config?.preferences ?? { guest_checkout_allowed: true, offer_autopay: true, enroll_during_payment: true, offer_paperless: true, reminder_channel: 'both', accepted_methods: config?.enabled_payment_capabilities ?? ['card', 'ach'], self_service_history: true, self_service_updates: true, fee_handling: 'mixed', preview: { default_device: 'desktop', enabled_scenarios: ['payment', 'history', 'communication', 'complex'] } }; }
// The fee the payer actually pays is total − amount (zero when the biller absorbs it), not the
// quote's raw feeCents which is reported for display even when the biller absorbs the fee.
function payerFeeCents(quote: PaymentQuote, amountCents: number) { return quote.totalCents - amountCents; }
function quoteFeeText(quote: PaymentQuote | undefined, amountCents: number) {
  if (!quote) return '…';
  const fee = payerFeeCents(quote, amountCents);
  return fee === 0 ? 'No payer fee' : `${money(fee)} fee`;
}
function reminderLabel(value: number | string) { return typeof value === 'number' ? ['Email', 'Text (SMS)', 'Both', 'None'][value] : humanize(value); }
function methodLabel(method: string) { return method === 'ach' ? 'Bank account (ACH)' : method === 'card' ? 'Card' : humanize(method); }
function humanize(value: string) { return value.replaceAll('_', ' ').replace(/\b\w/g, match => match.toUpperCase()); }
function paymentTermsLabel(mode: string | number | null | undefined, maximum?: number | null) { const installments = mode === 'installments_allowed' || mode === 1; if (!installments) return 'Pay in full'; return maximum ? `Up to ${maximum} installments` : 'Installments available'; }
function money(cents: number) { return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100); }
// Parse a payer-typed dollar amount to integer cents, or undefined when it isn't a clean money value.
// This lives in the stable core (never the authorable skin); the server re-validates the parsed cents.
function parseAmountCents(input: string): number | undefined {
  const trimmed = input.trim().replace(/^\$/, '').replace(/,/g, '');
  if (!/^\d+(\.\d{0,2})?$/.test(trimmed)) return undefined;
  const [dollars, fraction = ''] = trimmed.split('.');
  return Number(dollars) * 100 + Number(fraction.padEnd(2, '0'));
}
// Merge a fresh settlement pass over the accumulated outcomes: newer results (a retry that
// now succeeded, or a first attempt) replace the prior entry for the same invoice.
function mergeOutcomes(previous: BatchOutcome[], next: BatchOutcome[]): BatchOutcome[] {
  const byId = new Map(previous.map(entry => [entry.invoiceId, entry]));
  for (const entry of next) byId.set(entry.invoiceId, entry);
  return Array.from(byId.values());
}
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
    // Brand colors are evidence-gated and may be empty until chosen; the skin supplies defaults.
    !hasString(brand, 'primary_color', true) ||
    !hasString(brand, 'secondary_color', true) ||
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
    !hasString(pwa, 'theme_color', true) ||
    !hasString(pwa, 'background_color', true) ||
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
