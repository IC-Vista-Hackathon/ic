import { FormEvent, useMemo, useState } from 'react';
import { activityUrl, api } from './api';
import { logError, logEvent } from './telemetry';
import type { AgentActivity, Bootstrap, Deployment, ExperienceRevision, Session } from './types';

type Message = { role: 'assistant' | 'user'; content: string };

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

  const checklist = useMemo(() => [
    ['Brand reviewed', Boolean(draft)],
    ['Legal links reviewed', Boolean(draft?.definition.content.privacy_policy_url && draft.definition.content.terms_of_service_url)],
    ['Payment methods reviewed', Boolean(draft?.definition.enabled_payment_capabilities.length)],
    ['Compliance guidance acknowledged', approved],
  ] as const, [draft, approved]);

  async function createBiller(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError('');
    const data = new FormData(event.currentTarget);
    try {
      const created = await api.create({
        display_name: String(data.get('name')),
        slug: String(data.get('slug')),
        bill_type: String(data.get('billType')),
        postal_code: String(data.get('postalCode')),
        website: String(data.get('website')) || undefined,
      });
      setBootstrap(created); setDraft(created.draft); setSession(created.session);
      setMessages([{ role: 'assistant', content: `Welcome! I created a starting preview for ${created.biller.display_name}. Tell me what should change.` }]);
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
      setMessages(current => [...current, { role: 'assistant', content: response.reply }]); setDraft(response.draft); setSession(response.session);
    } catch (caught) { setError(toMessage(caught)); logError('studio.chat.failed', caught, { biller_id: bootstrap.biller.biller_id }); }
    finally { window.setTimeout(() => events.close(), 750); setBusy(false); }
  }

  async function approve() {
    if (!bootstrap || !draft) return; setBusy(true); setError('');
    try { const result = await api.approve(bootstrap.biller.biller_id, draft.revision); setDraft(result); setApproved(true); }
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
        if (current.state === 'failed' || current.state === 'rolled_back') { setError(current.failure_message ?? 'Publication failed.'); logError('studio.publication.terminal_failure', new Error(current.failure_message ?? current.state), { biller_id: billerId, deployment_id: deploymentId, failure_code: current.failure_code }); return; }
        await delay(2000);
      }
      throw new Error('Publication is still running. Refresh to check its status.');
    } catch (caught) { setError(toMessage(caught)); logError('studio.publication.monitor_failed', caught, { biller_id: billerId, deployment_id: deploymentId }); }
  }

  if (!bootstrap) return <Landing busy={busy} error={error} onSubmit={createBiller} />;
  return <main className="studio-shell">
    <header className="topbar"><div><span className="eyebrow">IC Biller Studio</span><h1>{bootstrap.biller.display_name}</h1></div><span className={`status status-${deployment?.state ?? session?.state}`}>{deployment ? humanize(deployment.state) : humanize(session?.state ?? 'collecting_information')}</span></header>
    {error && <div className="alert error" role="alert">{error}</div>}
    <AgentActivityStrip activity={agentActivity} busy={busy} />
    <div className="workspace">
      <section className="chat-panel" aria-label="Onboarding conversation">
        <div className="panel-heading"><h2>Build with your onboarding agent</h2><p>Describe outcomes in plain language. Changes appear in the preview.</p></div>
        <div className="messages" aria-live="polite">{messages.map((item, index) => <div className={`message ${item.role}`} key={`${item.role}-${index}`}><strong>{item.role === 'assistant' ? 'Studio' : 'You'}</strong><p>{item.content}</p></div>)}</div>
        <form className="composer" onSubmit={sendMessage}><label htmlFor="message">What should we change?</label><textarea id="message" value={message} onChange={e => setMessage(e.target.value)} rows={3} maxLength={4000}/><button disabled={busy || !message.trim()}>{busy ? 'Working…' : 'Send'}</button></form>
      </section>
      <section className="preview-panel" aria-label="Live customer preview">
        <div className="panel-heading row"><div><span className="eyebrow">Live preview</span><h2>Customer payment experience</h2></div><span className="preview-badge">Preview data</span></div>
        {draft && <PaymentPreview draft={draft} />}
      </section>
      <aside className="review-panel"><h2>Ready to publish?</h2><p>Publication stays locked until the biller explicitly reviews the experience.</p><ul className="checklist">{checklist.map(([label, done]) => <li key={label} className={done ? 'done' : ''}><span aria-hidden="true">{done ? '✓' : '○'}</span>{label}</li>)}</ul>
        {draft?.findings?.map(finding => <div className="alert guidance" key={finding.code}><strong>Guidance for review</strong><p>{finding.message}</p></div>)}
        {!approved ? <button className="secondary" disabled={busy || session?.state !== 'draft_ready'} onClick={approve}>Approve reviewed draft</button> : <button disabled={busy || Boolean(deployment)} onClick={publish}>{deployment ? 'Publication queued' : 'Publish experience'}</button>}
        {deployment && <div className="deployment"><strong>{deployment.state === 'ready' ? 'Experience published' : 'Publication request accepted'}</strong><span>{deployment.deployment_id}</span><span>State: {humanize(deployment.state)}</span>{deployment.published_url && <a href={deployment.published_url} target="_blank" rel="noreferrer">Open payer experience</a>}</div>}
      </aside>
    </div>
  </main>;
}

function Landing({ busy, error, onSubmit }: { busy: boolean; error: string; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  return <main className="landing"><section className="hero"><span className="eyebrow light">Payment Experience Builder</span><h1>Launch a branded payment experience in minutes</h1><p>Tell the Studio about your biller, shape the experience through conversation, preview it with demo data, and publish when it is ready.</p></section><form className="start-card" onSubmit={onSubmit}><h2>Start your experience</h2>{error && <div className="alert error" role="alert">{error}</div>}<label>Business name<input name="name" required defaultValue="City of Vista" /></label><div className="field-row"><label>URL slug<input name="slug" required pattern="[a-z0-9-]{3,63}" defaultValue="city-of-vista" /></label><label>Postal code<input name="postalCode" required inputMode="numeric" pattern="[0-9]{5}" defaultValue="02110" /></label></div><label>Bill type<select name="billType" defaultValue="Utility"><option>Utility</option><option>Property Tax</option><option>Insurance</option><option>General Invoice</option></select></label><label>Website <span>(optional)</span><input name="website" type="url" placeholder="https://example.gov" /></label><button disabled={busy}>{busy ? 'Creating preview…' : 'Build my preview'}</button><small>No payment credentials are collected. Compliance findings are guidance for review.</small></form></main>;
}

function AgentActivityStrip({ activity, busy }: { activity: AgentActivity[]; busy: boolean }) { const latest = [...activity].reverse().filter((item, index, all) => all.findIndex(candidate => candidate.agent_id === item.agent_id) === index).reverse(); return <section className="agent-strip" aria-live="polite"><div className="agent-title"><span className={`agent-pulse ${busy ? 'active' : ''}`} /><strong>{busy ? 'Agents working' : 'Agent activity'}</strong></div><div className="agent-list">{latest.length === 0 ? <span className="agent-empty">Ready for your next change</span> : latest.map(item => <span className={`agent-chip ${item.status}`} key={item.agent_id}><i>{item.status === 'completed' ? '✓' : item.status === 'failed' ? '!' : '•'}</i><span><strong>{item.display_name}</strong><small>{item.summary}</small></span></span>)}</div></section>; }
function PaymentPreview({ draft }: { draft: ExperienceRevision }) { const d = draft.definition; const action = d.ui?.actions.find(item => item.id === 'primary-payment-action'); return <div className={`device layout-${d.ui?.layout ?? 'centered-card'}`} style={{ '--brand': d.brand.primary_color, '--brand-secondary': d.brand.secondary_color } as React.CSSProperties}><div className="customer-header"><div className="logo-mark">{d.brand.display_name.split(' ').map(word => word[0]).slice(0, 2).join('')}</div><strong>{d.brand.display_name}</strong></div><div className="customer-body"><p className="muted">Account ••••4421</p><h3>{d.content.heading}</h3><p>{d.content.introduction}</p><div className="amount"><span>Amount due</span><strong>$128.42</strong><small>Due August 4</small></div><button style={{ background: d.brand.primary_color }}>{action?.label ?? 'Pay now'}</button><div className="methods">Accepted: {d.enabled_payment_capabilities.join(' · ')}</div><p className="support">{d.content.support_text}</p></div></div>; }
function humanize(value: string) { return value.replaceAll('_', ' ').replace(/\b\w/g, match => match.toUpperCase()); }
function toMessage(error: unknown) { return error instanceof Error ? error.message : 'The request failed.'; }
function delay(milliseconds: number) { return new Promise(resolve => window.setTimeout(resolve, milliseconds)); }
