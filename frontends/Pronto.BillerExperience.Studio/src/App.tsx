import { FormEvent, useMemo, useState } from 'react';
import { activityUrl, api } from './api';
import { logError, logEvent } from './telemetry';
import type { AgentActivity, Bootstrap, Deployment, ExperienceDefinition, ExperiencePreferences, ExperienceRevision, PreviewScenario, Session } from './types';

type Message = { role: 'assistant' | 'user'; content: string };
type PreviewDevice = 'desktop' | 'mobile';

export function App() {
  const [bootstrap, setBootstrap] = useState<Bootstrap>();
  const [draft, setDraft] = useState<ExperienceRevision>();
  const [session, setSession] = useState<Session>();
  const [messages, setMessages] = useState<Message[]>([]);
  const [message, setMessage] = useState('Make the experience welcoming, concise, and use #085368 as the primary color.');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [approved, setApproved] = useState(false);
  const [deployment, setDeployment] = useState<Deployment>();
  const [agentActivity, setAgentActivity] = useState<AgentActivity[]>([]);
  const [previewDevice, setPreviewDevice] = useState<PreviewDevice>('desktop');
  const [previewScenario, setPreviewScenario] = useState<PreviewScenario>('payment');

  const preferences = useMemo(() => experiencePreferences(draft?.definition), [draft]);
  const checklist = useMemo(() => [
    ['Brand reviewed', Boolean(draft)],
    ['Legal links reviewed', Boolean(draft?.definition.content.privacy_policy_url && draft.definition.content.terms_of_service_url)],
    ['Payment methods reviewed', preferences.accepted_methods.length > 0],
    ['Compliance guidance acknowledged', approved],
  ] as const, [draft, preferences, approved]);

  async function createBiller(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError('');
    const data = new FormData(event.currentTarget);
    try {
      const created = await api.create({ display_name: String(data.get('name')), slug: String(data.get('slug')), bill_type: String(data.get('billType')), postal_code: String(data.get('postalCode')), website: String(data.get('website')) || undefined });
      setBootstrap(created); setDraft(created.draft); setSession(created.session);
      setMessages([{ role: 'assistant', content: `Welcome! I created a starting preview for ${created.biller.display_name}. Tell me what should change.` }]);
      setPreviewDevice(experiencePreferences(created.draft.definition).preview.default_device);
      logEvent('studio.onboarding.started', { biller_id: created.biller.biller_id });
    } catch (caught) { setError(toMessage(caught)); logError('studio.onboarding.start_failed', caught); }
    finally { setBusy(false); }
  }

  async function sendMessage(event: FormEvent) {
    event.preventDefault(); if (!bootstrap || !message.trim()) return;
    const sent = message.trim(); setMessages(current => [...current, { role: 'user', content: sent }]); setMessage(''); setBusy(true); setError('');
    const events = new EventSource(activityUrl(bootstrap.biller.biller_id));
    events.addEventListener('agent_activity', raw => {
      const activity = JSON.parse((raw as MessageEvent).data) as AgentActivity;
      setAgentActivity(current => [...current.filter(item => item.event_id !== activity.event_id), activity].sort((a, b) => a.sequence - b.sequence));
    });
    try {
      const response = await api.chat(bootstrap.biller.biller_id, sent);
      setMessages(current => [...current, { role: 'assistant', content: response.reply }]); setDraft(response.draft); setSession(response.session); setApproved(false);
    } catch (caught) { setError(toMessage(caught)); logError('studio.chat.failed', caught, { biller_id: bootstrap.biller.biller_id }); }
    finally { window.setTimeout(() => events.close(), 750); setBusy(false); }
  }

  async function approve() {
    if (!bootstrap || !draft) return; setBusy(true); setError('');
    try { setDraft(await api.approve(bootstrap.biller.biller_id, draft.revision)); setApproved(true); }
    catch (caught) { setError(toMessage(caught)); logError('studio.approval.failed', caught); }
    finally { setBusy(false); }
  }

  async function publish() {
    if (!bootstrap || !draft) return; setBusy(true); setError('');
    try { const requested = await api.publish(bootstrap.biller.biller_id, draft.revision); setDeployment(requested); void monitorDeployment(bootstrap.biller.biller_id, requested.deployment_id); }
    catch (caught) { setError(toMessage(caught)); logError('studio.publication.failed', caught); }
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
    } catch (caught) { setError(toMessage(caught)); logError('studio.publication.monitor_failed', caught, { biller_id: billerId, deployment_id: deploymentId }); }
  }

  if (!bootstrap) return <Landing busy={busy} error={error} onSubmit={createBiller} />;
  return <main className="studio-shell">
    <header className="topbar"><div><span className="eyebrow">Pronto Biller Studio</span><h1>{bootstrap.biller.display_name}</h1></div><span className={`status status-${deployment?.state ?? session?.state}`}>{deployment ? humanize(deployment.state) : humanize(session?.state ?? 'collecting_information')}</span></header>
    {error && <div className="alert error global-alert" role="alert">{error}</div>}
    <AgentActivityStrip activity={agentActivity} busy={busy} />
    <div className="workspace">
      <section className="chat-panel" aria-label="Onboarding conversation">
        <div className="panel-heading"><span className="eyebrow">Conversation</span><h2>Build with your onboarding agent</h2><p>Describe outcomes in plain language. Every accepted change appears in the preview.</p></div>
        <div className="prompt-chips" aria-label="Suggested changes">{['Offer guest checkout', 'Disable AutoPay', 'Enable paperless', 'Use Pay Later'].map(prompt => <button type="button" key={prompt} onClick={() => setMessage(prompt)}>{prompt}</button>)}</div>
        <div className="messages" aria-live="polite">{messages.map((item, index) => <div className={`message ${item.role}`} key={`${item.role}-${index}`}><strong>{item.role === 'assistant' ? 'Studio' : 'You'}</strong><p>{item.content}</p></div>)}</div>
        <form className="composer" onSubmit={sendMessage}><label htmlFor="message">What should we change?</label><textarea id="message" value={message} onChange={event => setMessage(event.target.value)} rows={3} maxLength={4000}/><button disabled={busy || !message.trim()}>{busy ? 'Working…' : 'Send Change'}</button></form>
      </section>
      <section className="preview-panel" aria-label="Live customer preview">
        <div className="panel-heading preview-heading"><div><span className="eyebrow">Live preview</span><h2>Customer payment experience</h2></div><div className="device-switch" aria-label="Preview device"><button className={previewDevice === 'desktop' ? 'active' : ''} onClick={() => setPreviewDevice('desktop')}>Desktop</button><button className={previewDevice === 'mobile' ? 'active' : ''} onClick={() => setPreviewDevice('mobile')}>Mobile</button></div></div>
        <div className="scenario-tabs" role="tablist">{preferences.preview.enabled_scenarios.map(scenario => <button role="tab" aria-selected={previewScenario === scenario} className={previewScenario === scenario ? 'active' : ''} onClick={() => setPreviewScenario(scenario)} key={scenario}>{scenarioLabel(scenario)}</button>)}</div>
        {draft && <PaymentPreview draft={draft} preferences={preferences} device={previewDevice} scenario={previewScenario} />}
      </section>
      <aside className="review-panel">
        <span className="eyebrow">Experience definition</span><h2>Review recommendations</h2><p>Preferences are stored with the versioned biller definition and rendered by the shared payer application.</p>
        <PreferenceReview preferences={preferences} />
        <h3>Publish readiness</h3><ul className="checklist">{checklist.map(([label, done]) => <li key={label} className={done ? 'done' : ''}><span aria-hidden="true">{done ? '✓' : '○'}</span>{label}</li>)}</ul>
        {draft?.findings?.map(finding => <div className="alert guidance" key={finding.code}><strong>Guidance for review</strong><p>{finding.message}</p></div>)}
        {!approved ? <button className="secondary" disabled={busy || session?.state !== 'draft_ready'} onClick={approve}>Approve Draft</button> : <button disabled={busy || Boolean(deployment)} onClick={publish}>{deployment ? 'Publication Queued' : 'Publish Experience'}</button>}
        {deployment && <div className="deployment"><strong>{deployment.state === 'ready' ? 'Experience published' : 'Publication request accepted'}</strong><span>State: {humanize(deployment.state)}</span>{deployment.published_url && <a href={deployment.published_url} target="_blank" rel="noreferrer">Open Payer Experience</a>}</div>}
      </aside>
    </div>
  </main>;
}

function Landing({ busy, error, onSubmit }: { busy: boolean; error: string; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  return <main className="landing"><section className="hero"><span className="eyebrow light">Payment Experience Builder</span><h1>Launch a payment experience that feels like your brand.</h1><p>Describe your business, review AI-assisted recommendations, test payer scenarios, and publish a secure PWA in minutes.</p><div className="feature-list"><span>Compliance-aware</span><span>Branded by default</span><span>Real service integrations</span></div></section><form className="start-card" onSubmit={onSubmit}><span className="eyebrow">Get started</span><h2>Tell us about your business</h2>{error && <div className="alert error" role="alert">{error}</div>}<label>Business name<input name="name" required defaultValue="City of Vista" /></label><div className="field-row"><label>URL slug<input name="slug" required pattern="[a-z0-9-]{3,63}" defaultValue="city-of-vista" /></label><label>Postal code<input name="postalCode" required inputMode="numeric" pattern="[0-9]{5}" defaultValue="02110" /></label></div><label>Industry<select name="billType" defaultValue="Utility"><option>Utility</option><option>Property Tax</option><option>Insurance</option><option>General Invoice</option></select></label><label>Website <span>(optional)</span><input name="website" type="url" placeholder="https://example.gov" /></label><button disabled={busy}>{busy ? 'Building Preview…' : 'Build My Preview'}</button><small>No payment credentials are collected. Compliance findings remain guidance for biller review.</small></form></main>;
}

function AgentActivityStrip({ activity, busy }: { activity: AgentActivity[]; busy: boolean }) { const latest = [...activity].reverse().filter((item, index, all) => all.findIndex(candidate => candidate.agent_id === item.agent_id) === index).reverse(); return <section className="agent-strip" aria-live="polite"><div className="agent-title"><span className={`agent-pulse ${busy ? 'active' : ''}`} /><strong>{busy ? 'Agents working' : 'Agent activity'}</strong></div><div className="agent-list">{latest.length === 0 ? <span className="agent-empty">Research, Design, Accessibility, and Compliance are ready</span> : latest.map(item => <span className={`agent-chip ${item.status}`} key={item.agent_id}><i>{item.status === 'completed' ? '✓' : item.status === 'failed' ? '!' : '•'}</i><span><strong>{item.display_name}</strong><small>{item.summary}</small></span></span>)}</div></section>; }

function PreferenceReview({ preferences }: { preferences: ExperiencePreferences }) {
  const rows = [
    ['Guest checkout', yesNo(preferences.guest_checkout_allowed)], ['AutoPay', yesNo(preferences.offer_autopay)],
    ['Enroll at checkout', yesNo(preferences.enroll_during_payment)], ['Paperless', yesNo(preferences.offer_paperless)],
    ['Reminders', reminderLabel(preferences.reminder_channel)], ['Payment methods', preferences.accepted_methods.map(humanize).join(', ')],
    ['Account history', yesNo(preferences.self_service_history)], ['Account updates', yesNo(preferences.self_service_updates)],
    ['Fee handling', feeLabel(preferences.fee_handling)],
  ];
  return <div className="preference-review">{rows.map(([label, value]) => <div className="preference-row" key={label}><span>{label}</span><strong>{value}</strong>{preferences.recommendation_rationale?.[snake(label)] && <small>{preferences.recommendation_rationale[snake(label)]}</small>}</div>)}</div>;
}

function PaymentPreview({ draft, preferences, device, scenario }: { draft: ExperienceRevision; preferences: ExperiencePreferences; device: PreviewDevice; scenario: PreviewScenario }) {
  const definition = draft.definition; const action = definition.ui?.actions.find(item => item.id === 'primary-payment-action');
  return <div className={`preview-stage ${device}`}><div className={`browser-frame ${device}`} style={{ '--brand': definition.brand.primary_color, '--brand-secondary': definition.brand.secondary_color, fontFamily: definition.brand.font_family ?? 'Inter' } as React.CSSProperties}>
    <div className="browser-bar"><i/><i/><i/><span>pay.{slug(definition.brand.display_name)}.com</span></div>
    <div className="customer-header"><div className="logo-mark">{initials(definition.brand.display_name)}</div><strong>{definition.brand.display_name}</strong><span>Jordan Ellis<br/><small>Acct ····4421</small></span></div>
    <div className="customer-body">{scenario === 'payment' && <><div className="amount-hero"><span>Amount due · Due Aug 4</span><strong>$128.42</strong><button style={{ color: definition.brand.primary_color }}>{action?.label ?? 'Pay Now'}</button></div><h3>Recent statements</h3><Statement label="Utility Bill #240183" detail="Posted Jul 1" amount="$128.42" status="Due"/><Statement label="Utility Bill #239022" detail="Paid Jun 3" amount="$112.15" status="Paid"/></>}
      {scenario === 'history' && <><h2>Account history</h2><Statement label="Payment received" detail="Jun 1" amount="$112.15" status="Completed"/><Statement label="AutoPay enrollment" detail="Apr 3" amount="—" status="Completed"/><Statement label="Payment received" detail="Mar 1" amount="$104.90" status="Completed"/></>}
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
function toMessage(error: unknown) { return error instanceof Error ? error.message : 'The request failed.'; }
function delay(milliseconds: number) { return new Promise(resolve => window.setTimeout(resolve, milliseconds)); }
