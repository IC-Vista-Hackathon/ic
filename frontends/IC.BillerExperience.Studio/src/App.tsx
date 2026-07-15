import { FormEvent, useMemo, useState } from 'react';
import { activityUrl, api } from './api';
import { logError, logEvent } from './telemetry';
import { errorMessage, UiRequestError } from './http';
import type { AgentActivity, Bootstrap, Deployment, ExperienceDefinition, ExperiencePreferences, ExperienceRevision, PreviewInvoice, PreviewScenario, Session } from './types';

type Message = { role: 'assistant' | 'user'; content: string };
type PreviewDevice = 'desktop' | 'mobile';
type OperationError = { operation: string; message: string; retryable: boolean };

export function App() {
  const [bootstrap, setBootstrap] = useState<Bootstrap>();
  const [draft, setDraft] = useState<ExperienceRevision>();
  const [session, setSession] = useState<Session>();
  const [messages, setMessages] = useState<Message[]>([]);
  const [message, setMessage] = useState('Make the experience welcoming, concise, and use #085368 as the primary color.');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<OperationError>();
  const [activityConnection, setActivityConnection] = useState<'idle'|'connecting'|'connected'|'disconnected'>('idle');
  const [approved, setApproved] = useState(false);
  const [deployment, setDeployment] = useState<Deployment>();
  const [agentActivity, setAgentActivity] = useState<AgentActivity[]>([]);
  const [previewDevice, setPreviewDevice] = useState<PreviewDevice>('desktop');
  const [previewScenario, setPreviewScenario] = useState<PreviewScenario>('payment');
  const [previewInvoices, setPreviewInvoices] = useState<PreviewInvoice[]>([]);

  const preferences = useMemo(() => experiencePreferences(draft?.definition), [draft]);
  const checklist = useMemo(() => [
    ['Brand reviewed', Boolean(draft)],
    ['Legal links reviewed', Boolean(draft?.definition.content.privacy_policy_url && draft.definition.content.terms_of_service_url)],
    ['Payment methods reviewed', preferences.accepted_methods.length > 0],
    ['Compliance guidance acknowledged', approved],
  ] as const, [draft, preferences, approved]);

  async function createBiller(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(undefined);
    const data = new FormData(event.currentTarget);
    try {
      const created = await api.create({ display_name: String(data.get('name')), slug: String(data.get('slug')), bill_type: String(data.get('billType')), postal_code: String(data.get('postalCode')), website: String(data.get('website')) || undefined });
      setBootstrap(created); setDraft(created.draft); setSession(created.session);
      setMessages([{ role: 'assistant', content: `Welcome! I created a starting preview for ${created.biller.display_name}. Tell me what should change.` }]);
      setPreviewDevice(experiencePreferences(created.draft.definition).preview.default_device);
      const seeded = await api.invoices(created.biller.biller_id);
      setPreviewInvoices(seeded.invoices);
      logEvent('studio.onboarding.started', { biller_id: created.biller.biller_id });
    } catch (caught) { setFailure('Create biller', caught); logError('studio.onboarding.start_failed', caught); }
    finally { setBusy(false); }
  }

  async function sendMessage(event: FormEvent) {
    event.preventDefault(); if (!bootstrap || !message.trim()) return;
    const sent = message.trim(); setMessages(current => [...current, { role: 'user', content: sent }]); setMessage(''); setBusy(true); setError(undefined);
    const events = new EventSource(activityUrl(bootstrap.biller.biller_id));
    setActivityConnection('connecting');
    events.onopen = () => setActivityConnection('connected');
    events.addEventListener('agent_activity', raw => {
      try {
        const activity = JSON.parse((raw as MessageEvent).data) as AgentActivity;
        if (!activity.event_id || !activity.agent_id || !activity.status) throw new Error('Agent activity payload is incomplete.');
        setAgentActivity(current => [...current.filter(item => item.event_id !== activity.event_id), activity].sort((a, b) => a.sequence - b.sequence));
        if (activity.status === 'failed' || activity.status === 'degraded') logError('studio.agent.unhealthy', new Error(activity.summary), { biller_id: bootstrap.biller.biller_id, agent_id: activity.agent_id, run_id: activity.run_id, agent_status: activity.status, trace_id: activity.trace_id });
      } catch (caught) { logError('studio.activity.invalid_event', caught, { biller_id: bootstrap.biller.biller_id }); }
    });
    events.addEventListener('stream_error', raw => {
      try {
        const problem = JSON.parse((raw as MessageEvent).data) as { message?: string; trace_id?: string };
        setActivityConnection('disconnected');
        setError({ operation: 'Stream agent activity', message: `${problem.message ?? 'Live agent updates are temporarily unavailable.'}${problem.trace_id ? ` Reference: ${problem.trace_id}` : ''}`, retryable: true });
        logError('studio.activity.server_failed', new Error(problem.message ?? 'Agent activity stream failed.'), { biller_id: bootstrap.biller.biller_id, trace_id: problem.trace_id });
      } catch (caught) { logError('studio.activity.invalid_error_event', caught, { biller_id: bootstrap.biller.biller_id }); }
    });
    events.onerror = () => { setActivityConnection('disconnected'); logError('studio.activity.connection_failed', new Error('Agent activity stream disconnected.'), { biller_id: bootstrap.biller.biller_id }); };
    try {
      const response = await api.chat(bootstrap.biller.biller_id, sent);
      setMessages(current => [...current, { role: 'assistant', content: response.reply }]); setDraft(response.draft); setSession(response.session); setApproved(false);
    } catch (caught) { setFailure('Apply requested change', caught); logError('studio.chat.failed', caught, { biller_id: bootstrap.biller.biller_id }); }
    finally { window.setTimeout(() => { events.close(); setActivityConnection('idle'); }, 15_000); setBusy(false); }
  }

  async function approve() {
    if (!bootstrap || !draft) return; setBusy(true); setError(undefined);
    try { setDraft(await api.approve(bootstrap.biller.biller_id, draft.revision)); setApproved(true); }
    catch (caught) { setFailure('Approve draft', caught); logError('studio.approval.failed', caught); }
    finally { setBusy(false); }
  }

  async function updatePreferences(next: ExperiencePreferences) {
    if (!bootstrap || !draft) return; setBusy(true); setError(undefined);
    try {
      const updated = await api.update(bootstrap.biller.biller_id, { ...draft.definition, preferences: next }, draft.e_tag);
      setDraft(updated); setApproved(false);
      logEvent('studio.preferences.updated', { biller_id: bootstrap.biller.biller_id });
    } catch (caught) { setFailure('Update preferences', caught); logError('studio.preferences.update_failed', caught, { biller_id: bootstrap.biller.biller_id }); }
    finally { setBusy(false); }
  }

  async function publish() {
    if (!bootstrap || !draft) return; setBusy(true); setError(undefined);
    try { const requested = await api.publish(bootstrap.biller.biller_id, draft.revision); setDeployment(requested); void monitorDeployment(bootstrap.biller.biller_id, requested.deployment_id); }
    catch (caught) { setFailure('Publish experience', caught); logError('studio.publication.failed', caught); }
    finally { setBusy(false); }
  }

  async function monitorDeployment(billerId: string, deploymentId: string) {
    try {
      for (let attempt = 0; attempt < 60; attempt++) {
        const current = await api.deployment(billerId, deploymentId); setDeployment(current);
        if (current.state === 'ready') { logEvent('studio.publication.ready', { biller_id: billerId, deployment_id: deploymentId, published_url: current.published_url }); return; }
        if (current.state === 'failed' || current.state === 'rolled_back') throw new Error(current.failure_message ?? 'Publication failed.');
        await delay(2000);
      }
      throw new Error('Publication is still running. Refresh to check its status.');
    } catch (caught) { setFailure('Monitor publication', caught); logError('studio.publication.monitor_failed', caught, { biller_id: billerId, deployment_id: deploymentId }); }
  }

  function setFailure(operation: string, caught: unknown) { setError({ operation, message: errorMessage(caught), retryable: caught instanceof UiRequestError && caught.retryable }); }

  if (!bootstrap) return <Landing busy={busy} error={error} onSubmit={createBiller} />;
  return <main className="studio-shell">
    <header className="topbar"><div><span className="eyebrow">IC Biller Studio</span><h1>{bootstrap.biller.display_name}</h1></div><span className={`status status-${deployment?.state ?? session?.state}`}>{deployment ? humanize(deployment.state) : humanize(session?.state ?? 'collecting_information')}</span></header>
    {error && <div className="alert error global-alert" role="alert"><strong>{error.operation} failed</strong><span>{error.message}</span>{error.retryable && <small>You can safely try again; the last successful preview remains available.</small>}</div>}
    <AgentActivityStrip activity={agentActivity} busy={busy} connection={activityConnection} />
    <div className="workspace">
      <section className="chat-panel" aria-label="Onboarding conversation">
        <div className="panel-heading"><span className="eyebrow">Conversation</span><h2>Build with your onboarding agent</h2><p>Describe outcomes in plain language. Every accepted change appears in the preview.</p></div>
        <div className="prompt-chips" aria-label="Suggested changes">{['Offer guest checkout', 'Disable AutoPay', 'Enable paperless', 'Use Pay Later'].map(prompt => <button type="button" key={prompt} onClick={() => setMessage(prompt)}>{prompt}</button>)}</div>
        <div className="messages" aria-live="polite">{messages.map((item, index) => <div className={`message ${item.role}`} key={`${item.role}-${index}`}><strong>{item.role === 'assistant' ? 'Studio' : 'You'}</strong><p>{item.content}</p></div>)}</div>
        <OrchestrationOutput activity={agentActivity} busy={busy} />
        <form className="composer" onSubmit={sendMessage}><label htmlFor="message">What should we change?</label><textarea id="message" value={message} onChange={event => setMessage(event.target.value)} rows={3} maxLength={4000}/><button disabled={busy || !message.trim()}>{busy ? 'Working…' : 'Send Change'}</button></form>
      </section>
      <section className="preview-panel" aria-label="Live customer preview">
        <div className="panel-heading preview-heading"><div><span className="eyebrow">Live preview</span><h2>Customer payment experience</h2></div><div className="device-switch" aria-label="Preview device"><button className={previewDevice === 'desktop' ? 'active' : ''} onClick={() => setPreviewDevice('desktop')}>Desktop</button><button className={previewDevice === 'mobile' ? 'active' : ''} onClick={() => setPreviewDevice('mobile')}>Mobile</button></div></div>
        <div className="scenario-tabs" role="tablist">{preferences.preview.enabled_scenarios.map(scenario => <button role="tab" aria-selected={previewScenario === scenario} className={previewScenario === scenario ? 'active' : ''} onClick={() => setPreviewScenario(scenario)} key={scenario}>{scenarioLabel(scenario)}</button>)}</div>
        {draft && <PaymentPreview draft={draft} preferences={preferences} invoices={previewInvoices} device={previewDevice} scenario={previewScenario} />}
      </section>
      <aside className="review-panel">
        <span className="eyebrow">Experience definition</span><h2>Review recommendations</h2><p>Preferences are stored with the versioned biller definition and rendered by the shared payer application.</p>
        <PreferenceReview preferences={preferences} capabilities={draft?.definition.enabled_payment_capabilities ?? []} busy={busy} onChange={updatePreferences} />
        <h3>Publish readiness</h3><ul className="checklist">{checklist.map(([label, done]) => <li key={label} className={done ? 'done' : ''}><span aria-hidden="true">{done ? '✓' : '○'}</span>{label}</li>)}</ul>
        {draft?.findings?.map(finding => <div className="alert guidance" key={finding.code}><strong>Guidance for review</strong><p>{finding.message}</p></div>)}
        {!approved ? <button className="secondary" disabled={busy || session?.state !== 'draft_ready'} onClick={approve}>Approve Draft</button> : <button disabled={busy || Boolean(deployment)} onClick={publish}>{deployment ? 'Publication Queued' : 'Publish Experience'}</button>}
        {deployment && <div className="deployment"><strong>{deployment.state === 'ready' ? 'Experience published' : 'Publication request accepted'}</strong><span>State: {humanize(deployment.state)}</span>{deployment.published_url && <a href={deployment.published_url} target="_blank" rel="noreferrer">Open Payer Experience</a>}</div>}
      </aside>
    </div>
  </main>;
}

function Landing({ busy, error, onSubmit }: { busy: boolean; error?: OperationError; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  const [started, setStarted] = useState(false);
  const [step, setStep] = useState(0);
  const [name, setName] = useState('City of Vista');
  const [slugValue, setSlugValue] = useState('city-of-vista');
  const [postalCode, setPostalCode] = useState('02110');
  const [billType, setBillType] = useState('Utility');
  const [website, setWebsite] = useState('');
  if (!started) return <main className="handoff-landing">
    <header className="handoff-header"><strong>InvoiceCloud</strong><span>Payment Experience Builder</span></header>
    <section className="handoff-hero"><span className="eyebrow light">InvoiceCloud · Try it out</span><h1>Build your own custom payment experience</h1><p>Answer a few questions about your business. Our agents will check your requirements, match your brand, and generate a live, service-backed preview.</p><button type="button" onClick={() => setStarted(true)}>Try It Out</button><small>No account or credit card required to preview</small></section>
    <section className="handoff-features">{[
      ['Compliance-aware', 'Review location-aware payment guidance before publishing.'],
      ['Matches your brand', 'Turn your identity, colors, and language into a versioned experience.'],
      ['Agent-built, human-approved', 'Watch each specialist agent work and keep control of every change.'],
    ].map(([title, copy], index) => <article key={title}><i>{['✓','◇','✦'][index]}</i><h2>{title}</h2><p>{copy}</p></article>)}</section>
  </main>;

  const steps = ['Business type', 'Business details', 'Brand details', 'Review'];
  return <main className="wizard-page"><div className="wordmark">InvoiceCloud</div><form className="wizard-card" onSubmit={onSubmit}>
    <div className="wizard-progress"><div><span style={{ width: `${((step + 1) / steps.length) * 100}%` }}/></div><ol>{steps.map((label, index) => <li className={index <= step ? 'active' : ''} key={label}>{label}</li>)}</ol></div>
    {error && <div className="alert error" role="alert"><strong>{error.operation} failed</strong><span>{error.message}</span>{error.retryable && <small>Please try again.</small>}</div>}
    {step === 0 && <section><h1>What line of business is this for?</h1><p>We’ll tailor terminology, recommendations, and the payer preview to your business.</p><div className="choice-list">{['Utility','Property Tax','Insurance','General Invoice'].map(value => <button type="button" className={billType === value ? 'selected' : ''} onClick={() => setBillType(value)} key={value}><strong>{value}</strong><small>{value === 'Utility' ? 'Water, electric, gas, or municipal services' : value === 'Property Tax' ? 'Local tax and assessment payments' : value === 'Insurance' ? 'Premiums and policyholder billing' : 'Flexible invoice and account payments'}</small></button>)}</div></section>}
    {step === 1 && <section><h1>Tell us about the business</h1><p>This becomes the identity and URL of the generated experience.</p><label>Business name<input value={name} onChange={event => { setName(event.target.value); setSlugValue(slug(event.target.value)); }} autoFocus /></label><div className="field-row"><label>URL slug<input value={slugValue} onChange={event => setSlugValue(event.target.value)} pattern="[a-z0-9-]{3,63}" /></label><label>Postal code<input value={postalCode} onChange={event => setPostalCode(event.target.value)} inputMode="numeric" pattern="[0-9]{5}" /></label></div></section>}
    {step === 2 && <section><h1>Brand details</h1><p>Share a website and the design agent will use it as context for the branded preview.</p><label>Website <span>(optional)</span><input value={website} onChange={event => setWebsite(event.target.value)} type="url" placeholder="https://example.gov" autoFocus /></label><div className="smart-defaults"><strong>No website yet?</strong><span>Leave this blank and the agents will begin with accessible InvoiceCloud design-system defaults. You can refine colors, type, and content in Studio.</span></div></section>}
    {step === 3 && <section><h1>Ready to build</h1><p>Review the starting context. You can edit every agent recommendation before publishing.</p><dl className="intake-review"><div><dt>Business</dt><dd>{name}</dd></div><div><dt>Line of business</dt><dd>{billType}</dd></div><div><dt>Experience URL</dt><dd>pay.invoicecloud.com/{slugValue}</dd></div><div><dt>Brand source</dt><dd>{website || 'Smart defaults'}</dd></div></dl><div className="agent-promise"><i>✦</i><span><strong>The agent run remains visible</strong><small>Research, Experience Design, Accessibility, and Compliance output will stream into the Studio workspace.</small></span></div></section>}
    <input type="hidden" name="name" value={name}/><input type="hidden" name="slug" value={slugValue}/><input type="hidden" name="postalCode" value={postalCode}/><input type="hidden" name="billType" value={billType}/><input type="hidden" name="website" value={website}/>
    <footer><button className="wizard-back" type="button" onClick={() => step === 0 ? setStarted(false) : setStep(current => current - 1)}>Back</button>{step < steps.length - 1 ? <button type="button" disabled={(step === 1 && (!name || !slugValue || !/^\d{5}$/.test(postalCode)))} onClick={() => setStep(current => current + 1)}>Continue</button> : <button disabled={busy}>{busy ? 'Building Preview…' : 'Build My Preview'}</button>}</footer>
  </form></main>;
}

function AgentActivityStrip({ activity, busy, connection }: { activity: AgentActivity[]; busy: boolean; connection: 'idle'|'connecting'|'connected'|'disconnected' }) {
  const latest = [...activity].reverse().filter((item, index, all) => all.findIndex(candidate => candidate.agent_id === item.agent_id) === index).reverse();
  return <section className={`agent-strip ${busy ? 'analyzing' : ''}`} aria-live="polite">
    <div className="agent-title"><span className={`agent-pulse ${busy ? 'active' : ''}`} /><span><strong>{busy ? 'Building your preview…' : 'Foundry agent activity'}</strong><small>{busy ? 'Orchestration is delegating work to the eligible agents below.' : 'The last known state remains visible.'}</small></span>{connection !== 'idle' && <small className={`stream-${connection}`}>{connection === 'disconnected' ? 'Updates disconnected' : `Updates ${connection}`}</small>}</div>
    <div className="agent-list">{latest.length === 0 ? <span className="agent-empty">Waiting for orchestration to discover eligible Foundry agents.</span> : latest.map(item => <article className={`agent-chip ${item.status}`} key={item.agent_id}><i>{activityIcon(item.status)}</i><span><strong>{item.display_name}</strong><code title={item.agent_id}>{shortAgentId(item.agent_id)}</code><small>{item.summary}</small>{item.duration_ms !== undefined && <em>{Math.round(item.duration_ms)} ms</em>}</span></article>)}</div>
  </section>;
}

function OrchestrationOutput({ activity, busy }: { activity: AgentActivity[]; busy: boolean }) {
  return <details className="orchestration-output" open={busy || activity.length > 0}><summary><span><i className={busy ? 'running' : ''}/><strong>Agent orchestration output</strong></span><small>{activity.length ? `${activity.length} events` : 'Ready'}</small></summary><div className="orchestration-events">{activity.length === 0 ? <p>Agent discovery, delegation, tool status, and trace IDs will appear here as the experience is generated.</p> : activity.map(item => <article className={item.status} key={item.event_id}><i>{activityIcon(item.status)}</i><span><strong>{item.display_name} <code>{shortAgentId(item.agent_id)}</code></strong><p>{item.summary}</p><small>{humanize(item.status)} · {new Date(item.occurred_at).toLocaleTimeString()}{item.duration_ms !== undefined ? ` · ${Math.round(item.duration_ms)} ms` : ''}{item.error_code ? ` · ${item.error_code}` : ''}{item.trace_id ? ` · trace ${item.trace_id}` : ''}</small></span></article>)}</div></details>;
}

function activityIcon(status: AgentActivity['status']) { return status === 'completed' ? '✓' : status === 'failed' || status === 'degraded' ? '!' : status === 'discovered' ? '⌕' : '•'; }
function shortAgentId(id: string) { return id.length > 20 ? `${id.slice(0, 8)}…${id.slice(-6)}` : id; }

function PreferenceReview({ preferences, capabilities, busy, onChange }: { preferences: ExperiencePreferences; capabilities: string[]; busy: boolean; onChange: (next: ExperiencePreferences) => void }) {
  const rows = [
    ['Guest checkout', yesNo(preferences.guest_checkout_allowed)], ['AutoPay', yesNo(preferences.offer_autopay)],
    ['Enroll at checkout', yesNo(preferences.enroll_during_payment)], ['Paperless', yesNo(preferences.offer_paperless)],
    ['Reminders', reminderLabel(preferences.reminder_channel)], ['Payment methods', preferences.accepted_methods.map(humanize).join(', ')],
    ['Account history', yesNo(preferences.self_service_history)], ['Account updates', yesNo(preferences.self_service_updates)],
    ['Fee handling', feeLabel(preferences.fee_handling)],
  ];
  const toggle = (key: 'guest_checkout_allowed'|'offer_autopay'|'enroll_during_payment'|'offer_paperless'|'self_service_history'|'self_service_updates') => onChange({ ...preferences, [key]: !preferences[key] });
  return <div className="preference-review">{rows.map(([label, value], index) => <div className="preference-row" key={label}><span>{label}</span><strong>{value}</strong>{index < 4 && <button disabled={busy} onClick={() => toggle((['guest_checkout_allowed','offer_autopay','enroll_during_payment','offer_paperless'] as const)[index])}>Change</button>}{index === 6 && <button disabled={busy} onClick={() => toggle('self_service_history')}>Change</button>}{index === 7 && <button disabled={busy} onClick={() => toggle('self_service_updates')}>Change</button>}{preferences.recommendation_rationale?.[snake(label)] && <small>{preferences.recommendation_rationale[snake(label)]}</small>}</div>)}<div className="preference-editor"><label>Reminder channel<select value={String(preferences.reminder_channel)} onChange={event => onChange({ ...preferences, reminder_channel: parseEnum(event.target.value) })}><option value="0">Email</option><option value="1">Text</option><option value="2">Both</option><option value="3">None</option></select></label><fieldset><legend>Accepted methods</legend>{capabilities.map(method => <label key={method}><input type="checkbox" checked={preferences.accepted_methods.includes(method)} onChange={() => onChange({ ...preferences, accepted_methods: preferences.accepted_methods.includes(method) ? preferences.accepted_methods.filter(item => item !== method) : [...preferences.accepted_methods, method] })}/>{humanize(method)}</label>)}</fieldset></div></div>;
}

function PaymentPreview({ draft, preferences, invoices, device, scenario }: { draft: ExperienceRevision; preferences: ExperiencePreferences; invoices: PreviewInvoice[]; device: PreviewDevice; scenario: PreviewScenario }) {
  const definition = draft.definition; const action = definition.ui?.actions.find(item => item.id === 'primary-payment-action');
  const current = invoices.find(invoice => invoice.status !== 'paid') ?? invoices[0];
  return <div className={`preview-stage ${device}`}><div className={`browser-frame ${device}`} style={{ '--brand': definition.brand.primary_color, '--brand-secondary': definition.brand.secondary_color, fontFamily: definition.brand.font_family ?? 'Inter' } as React.CSSProperties}>
    <div className="browser-bar"><i/><i/><i/><span>pay.{slug(definition.brand.display_name)}.com</span></div>
    <div className="customer-header"><div className="logo-mark">{initials(definition.brand.display_name)}</div><strong>{definition.brand.display_name}</strong><span>{current?.payer_name ?? 'Demo payer'}<br/><small>Acct ····{current?.account_number.slice(-4) ?? '4421'}</small></span></div>
    <div className="customer-body">{scenario === 'payment' && <><div className="amount-hero"><span>Amount due · {current ? new Date(current.due_date).toLocaleDateString() : 'Loading'}</span><strong>{current ? money(current.amount_cents) : '—'}</strong><button style={{ color: definition.brand.primary_color }}>{action?.label ?? 'Pay Now'}</button></div><h3>Recent statements</h3>{invoices.map(invoice => <Statement key={invoice.id} label={invoice.description} detail={new Date(invoice.due_date).toLocaleDateString()} amount={money(invoice.amount_cents)} status={humanize(invoice.status)}/>)}</>}
      {scenario === 'history' && <><h2>Account history</h2>{invoices.map(invoice => <Statement key={invoice.id} label={invoice.description} detail={new Date(invoice.due_date).toLocaleDateString()} amount={money(invoice.amount_cents)} status={humanize(invoice.status)}/>)}</>}
      {scenario === 'communication' && <><h2>Communication preferences</h2><PreferenceCard label="Payment reminders" value={reminderLabel(preferences.reminder_channel)}/><PreferenceCard label="Paperless billing" value={yesNo(preferences.offer_paperless)}/><PreferenceCard label="AutoPay" value={yesNo(preferences.offer_autopay)}/></>}
      {scenario === 'complex' && <><h2>Scenario preview</h2><div className="scenario-result"><strong>Delinquent payment</strong><p>The payer sees the outstanding balance, due-date context, supported methods, and biller-approved guidance without changing payment rails.</p></div></>}
      <div className="methods">Accepted: {preferences.accepted_methods.map(humanize).join(' · ')}</div>
    </div>
  </div></div>;
}

function Statement({ label, detail, amount, status }: { label: string; detail: string; amount: string; status: string }) { return <div className="statement"><span><strong>{label}</strong><small>{detail}</small></span><span><strong>{amount}</strong><small className={status === 'Due' ? 'due' : 'paid'}>{status}</small></span></div>; }
function PreferenceCard({ label, value }: { label: string; value: string }) { return <div className="payer-preference"><span>{label}</span><strong>{value}</strong></div>; }

function experiencePreferences(definition?: ExperienceDefinition): ExperiencePreferences { return definition?.preferences ?? { guest_checkout_allowed: true, offer_autopay: true, enroll_during_payment: true, offer_paperless: true, reminder_channel: 'both', accepted_methods: definition?.enabled_payment_capabilities ?? ['card', 'ach'], self_service_history: true, self_service_updates: true, fee_handling: 'mixed', preview: { default_device: 'desktop', enabled_scenarios: ['payment', 'history', 'communication', 'complex'] } }; }
function reminderLabel(value: ExperiencePreferences['reminder_channel']) { return typeof value === 'number' ? ['Email', 'Text (SMS)', 'Both', 'None'][value] : ({ email: 'Email', text: 'Text (SMS)', both: 'Both', none: 'None' }[value] ?? humanize(value)); }
function feeLabel(value: ExperiencePreferences['fee_handling']) { return typeof value === 'number' ? ['Absorb fees', 'Charge payer', 'Rules by method', 'Not decided'][value] : ({ absorb: 'Absorb fees', charge: 'Charge payer', mixed: 'Rules by method', undecided: 'Not decided' }[value] ?? humanize(value)); }
function scenarioLabel(value: PreviewScenario) { return ({ payment: 'Make a Payment', history: 'Account History', communication: 'Preferences', complex: 'Complex Scenario' })[value]; }
function yesNo(value: boolean) { return value ? 'Yes' : 'No'; }
function snake(value: string) { return value.toLowerCase().replaceAll(' ', '_'); }
function slug(value: string) { return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, ''); }
function initials(value: string) { return value.split(' ').map(word => word[0]).slice(0, 2).join(''); }
function humanize(value: string) { return value.replaceAll('_', ' ').replace(/\b\w/g, match => match.toUpperCase()); }
function delay(milliseconds: number) { return new Promise(resolve => window.setTimeout(resolve, milliseconds)); }
function parseEnum(value: string): ExperiencePreferences['reminder_channel'] { return Number(value); }
function money(cents: number) { return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(cents / 100); }
