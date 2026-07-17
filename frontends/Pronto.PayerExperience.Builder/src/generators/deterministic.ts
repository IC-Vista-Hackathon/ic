import type { DesignBrief, GeneratedSkin } from '../types';
import type { SkinGenerator } from './index';

// Offline, no-network generator. Derives a genuinely different-looking skin from
// the brand tokens + a deterministic style seed, so the whole pipeline (generate ->
// build -> validate -> publish) is runnable and testable without a model call.
// It restyles the stable class names the core renders and authors the presentational
// structure of the multi-invoice selection/cart/batch review flow (flow.tsx, feature F3).
export class DeterministicSkinGenerator implements SkinGenerator {
  readonly name = 'deterministic';

  async generate(brief: DesignBrief): Promise<GeneratedSkin> {
    const seed = hash(`${brief.biller_slug}:${brief.brand_keywords.join(',')}`);
    const radius = [8, 12, 18, 24][seed % 4];
    const surface = ['#ffffff', '#fbfcfd', '#f7f9fb'][seed % 3];
    const canvas = ['#eef2f4', '#f4f6f7', '#eef1f6'][(seed >> 2) % 3];
    const headerStyle = seed % 2 === 0
      ? `linear-gradient(135deg, ${brief.primary_color}, ${brief.secondary_color})`
      : brief.primary_color;
    const fontStack = `${JSON.stringify(brief.font_family)},Inter,ui-sans-serif,system-ui,-apple-system,sans-serif`;

    return {
      themeCss: themeCss({ radius, surface, canvas, headerStyle, fontStack, brief }),
      chromeTsx: chromeTsx(brief),
      flowTsx: flowTsx(brief),
      notes: `deterministic seed=${seed} radius=${radius}`,
    };
  }
}

function themeCss(o: {
  radius: number;
  surface: string;
  canvas: string;
  headerStyle: string;
  fontStack: string;
  brief: DesignBrief;
}): string {
  return `/* Generated skin for ${commentSafe(o.brief.display_name)} (${commentSafe(o.brief.biller_slug)}) — deterministic. */
:root{font-family:${o.fontStack};--brand:${o.brief.primary_color};--brand-secondary:${o.brief.secondary_color};--radius:${o.radius}px;color:#1c1c1c;background:${o.canvas};line-height:1.55}
*{box-sizing:border-box}body{margin:0}button,input{font:inherit}:focus-visible{outline:2.5px solid var(--brand);outline-offset:3px}
.app{min-height:100vh;display:flex;flex-direction:column}
.app>header{background:${o.headerStyle};color:#fff;padding:1.1rem max(1rem,calc((100% - 760px)/2));display:flex;align-items:center;gap:.8rem}
.app>header span{margin-left:auto;font-size:.8rem;opacity:.9}
.mark{width:42px;height:42px;border-radius:calc(var(--radius) - 2px);background:#fff;color:var(--brand);display:grid;place-items:center;font-weight:800}
.hero-tag{margin-left:.4rem;font-size:.72rem;text-transform:uppercase;letter-spacing:.08em;background:rgba(255,255,255,.18);padding:.2rem .55rem;border-radius:999px}
.account-nav{background:${o.surface};border-bottom:1px solid #dfe5e8;padding:0 max(1rem,calc((100% - 760px)/2));display:flex;gap:.25rem;overflow:auto}
.account-nav button{border:0;border-bottom:3px solid transparent;background:none;color:#536068;padding:.9rem .8rem;font-weight:700;white-space:nowrap;cursor:pointer}
.account-nav button.active{color:var(--brand);border-bottom-color:var(--brand)}
.app>main{width:min(720px,calc(100% - 2rem));margin:2.5rem auto;flex:1}
.intro .eyebrow{display:inline-block;color:var(--brand);font-weight:700;font-size:.78rem;text-transform:uppercase;letter-spacing:.06em}
.intro h1{font-size:2.1rem;margin:.25rem 0}.intro span,.card-copy{color:#596268}
.card{background:${o.surface};border:1px solid #dfe5e8;border-radius:var(--radius);box-shadow:0 6px 22px #1b2d3512;padding:1.5rem;margin-top:1.4rem;display:grid;gap:1rem}
.card h2{margin:0;font-size:1.25rem}
.card label:not(.check){display:grid;gap:.4rem;font-weight:650}
.card input[type=text],.card input[type=email],.card input:not([type]){border:1px solid #98a4aa;border-radius:calc(var(--radius) - 4px);padding:.85rem}
.card button{border:0;border-radius:calc(var(--radius) - 4px);padding:.9rem 1rem;background:var(--brand);color:#fff;font-weight:750;cursor:pointer}
.card button:disabled{opacity:.55}
.bill{background:${o.headerStyle};color:#fff;padding:1.15rem;border-radius:calc(var(--radius) - 2px);display:flex;justify-content:space-between;align-items:center}
.bill div{display:grid}.bill small{color:#eaf6f8}.bill>strong{font:700 1.5rem 'Roboto Mono',ui-monospace,monospace}
.choices{display:grid;grid-template-columns:1fr 1fr;gap:.75rem}
.choices button{background:${o.surface};color:#1c1c1c;border:1px solid #aeb7bc;display:grid;text-align:left}
.choices button.selected{background:color-mix(in srgb,var(--brand) 12%,#fff);color:var(--brand);border:2px solid var(--brand)}
.choices small{font-weight:400}
.method-chips{font-size:.8rem;color:#596268;background:#f2f5f6;padding:.6rem .8rem;border-radius:8px}
fieldset{border:0;padding:0;display:grid;gap:.6rem}legend{font-weight:700;margin-bottom:.5rem}
.check{display:flex;gap:.7rem;padding:.75rem;border:1px solid #dfe5e8;border-radius:calc(var(--radius) - 6px)}
.check input{width:20px;height:20px;accent-color:var(--brand)}.check span{display:grid}.check small{color:#596268}
.card dl{margin:0;display:grid;gap:.7rem}.card dl div{display:flex;justify-content:space-between}
.card dl dd{font:700 1rem 'Roboto Mono',ui-monospace,monospace}
.card dl .total{border-top:1px solid #d1d8dc;padding-top:.8rem;font-size:1.15rem}
.notice{background:color-mix(in srgb,var(--brand) 10%,#fff);color:var(--brand);padding:.8rem;border-radius:8px}
.consent{font-size:.82rem;color:#596268}
.actions{display:grid;grid-template-columns:1fr 2fr;gap:.7rem}
.actions button.back{background:${o.surface};color:var(--brand);border:1px solid var(--brand)}
.success{text-align:center}
.success-icon{width:58px;height:58px;border-radius:50%;background:#e3f4e6;color:#23612d;display:grid;place-items:center;font-size:1.8rem;margin:auto}
.pill{display:inline-block;background:#e3f4e6;color:#23612d;border-radius:999px;padding:.3rem .7rem;margin:.2rem}
.history-row{display:flex;justify-content:space-between;border-top:1px solid #dfe5e8;padding-top:1rem}
.preference-summary{display:grid;gap:.7rem}
.preference-summary>span{display:flex;justify-content:space-between;border:1px solid #dfe5e8;border-radius:calc(var(--radius) - 6px);padding:.8rem}
.alert{background:#f8e4e3;color:#8b2421;padding:.8rem;border-radius:8px;margin-top:1rem}
.center{min-height:100vh;display:grid;place-items:center}
.select-head{display:flex;justify-content:space-between;align-items:center}
.link{background:none;border:0;color:var(--brand);font-weight:700;cursor:pointer;padding:.2rem}
.invoice-list,.cart-lines,.batch-lines{list-style:none;margin:0;padding:0;display:grid;gap:.6rem}
.invoice-option{display:flex;gap:.7rem;align-items:flex-start;padding:.85rem;border:1px solid #dfe5e8;border-radius:calc(var(--radius) - 6px)}
.invoice-body{display:grid;gap:.15rem;flex:1}.invoice-title{display:flex;gap:.5rem;align-items:center}.invoice-body small{color:#596268}
.invoice-right{display:flex;align-items:center;gap:.5rem;margin-left:auto}
.bill-type{align-self:start;font-size:.7rem;font-weight:800;letter-spacing:.04em;text-transform:uppercase;background:color-mix(in srgb,var(--brand) 14%,#fff);color:var(--brand);padding:.15rem .5rem;border-radius:999px}
.status-dot{width:.7rem;height:.7rem;border-radius:50%;flex:none}.status-green{background:#3ecf8e}.status-yellow{background:#f5c542}.status-red{background:#ef5f5f}
.bill-note{font-size:.85rem;color:#596268}.bill-note-strong{font-size:.85rem;font-weight:800;color:var(--brand)}
.cart-line{display:flex;justify-content:space-between;align-items:center;border-top:1px solid #eef1f3;padding-top:.6rem}.cart-line:first-child{border-top:0;padding-top:0}
.cart-line-right{display:flex;gap:.7rem;align-items:center}
.batch-line{display:flex;justify-content:space-between;gap:1rem;border-top:1px solid #eef1f3;padding-top:.7rem;align-items:flex-start}.batch-line:first-child{border-top:0;padding-top:0}
.batch-line-desc{display:grid;gap:.15rem}.batch-line-amounts{display:grid;justify-items:end;text-align:right}.batch-line-amounts small{color:#596268}
.batch-line-status{font-weight:700}.batch-line-paid .batch-line-status{color:#23612d}.batch-line-failed .batch-line-status{color:#8b2421}
.app>footer{background:${o.surface};border-top:1px solid #dfe5e8;padding:1rem max(1rem,calc((100% - 760px)/2));display:flex;justify-content:space-between;color:#596268;font-size:.8rem}
.app>footer nav{display:flex;gap:1rem}.app>footer a{color:var(--brand)}
@media(max-width:540px){.choices{grid-template-columns:1fr}.app>main{margin:1.5rem auto}.app>footer{display:grid;gap:.6rem}.intro h1{font-size:1.7rem}.app>header span{display:none}.preference-summary>span{display:grid}}
@media(prefers-reduced-motion:reduce){*,*::before,*::after{scroll-behavior:auto!important;transition:none!important;animation:none!important}}
`;
}

function chromeTsx(brief: DesignBrief): string {
  const eyebrow = brief.bill_type ? `${titleCase(brief.bill_type)} services` : 'Account services';
  const tag = brief.brand_keywords.includes('civic') ? 'Official payments' : 'Secure payments';
  return `import type { FooterProps, HeaderProps, IntroProps } from './contract';

// Generated skin chrome for ${commentSafe(brief.display_name)} (${commentSafe(brief.biller_slug)}).
// Presentational only — no fetch, no payment logic. Signatures come from contract.ts.

export function Header({ brand }: HeaderProps) {
  return (
    <header data-testid="app-header" style={{ background: brand.primary_color }}>
      <div className="mark">{initials(brand.display_name)}</div>
      <strong>{brand.display_name}</strong>
      <span className="hero-tag">${tag}</span>
    </header>
  );
}

export function Intro({ eyebrow, heading, subheading }: IntroProps) {
  return (
    <div className="intro">
      <span className="eyebrow">{eyebrow || ${JSON.stringify(eyebrow)}}</span>
      <h1>{heading}</h1>
      <span>{subheading}</span>
    </div>
  );
}

export function Footer({ brand, content }: FooterProps) {
  return (
    <footer>
      <span>{content.support_text}</span>
      <nav>
        <a href={content.privacy_policy_url}>Privacy</a>
        <a href={content.terms_of_service_url}>Terms</a>
      </nav>
    </footer>
  );
}

function initials(name: string) {
  return name.split(' ').map(word => word[0]).slice(0, 2).join('');
}
`;
}

// Authors src/skin/flow.tsx — the presentational STRUCTURE of the multi-invoice
// selection list, cart, and batch review (feature F3). Imports types only from ./contract;
// no fetch, no payment logic, no fee/total math. The core hands fully preformatted view
// models + callbacks, so a generated flow can restructure freely while money stays in core.
export function flowTsx(brief: DesignBrief): string {
  const noun = brief.bill_type ? `${titleCase(brief.bill_type)} bills` : 'bills';
  return `import type {
  BatchReviewProps,
  CartProps,
  InvoiceSelectListProps,
  SelectableInvoice,
} from './contract';

// Generated skin flow for ${commentSafe(brief.display_name)} (${commentSafe(brief.biller_slug)}).
// Presentational only — no fetch, no payment logic, no money math. Feature F3.

export function InvoiceSelectList({ heading, invoices, onToggle, onSelectAll, onClearAll, allSelected }: InvoiceSelectListProps) {
  return (
    <section className="card" aria-label={heading} data-testid="invoice-select">
      <div className="select-head">
        <h2>{heading}</h2>
        <button type="button" className="link" data-testid={allSelected ? 'clear-all' : 'select-all'} onClick={allSelected ? onClearAll : onSelectAll}>
          {allSelected ? 'Clear all' : 'Select all ${commentSafe(noun)}'}
        </button>
      </div>
      <ul className="invoice-list" role="list">
        {invoices.map(invoice => (
          <li key={invoice.id}>
            <label className="check invoice-option" data-testid={\`invoice-option-\${invoice.id}\`}>
              <input
                type="checkbox"
                checked={invoice.selected}
                onChange={() => onToggle(invoice.id)}
                aria-label={\`Pay \${invoice.typeLabel ? \`\${invoice.typeLabel} — \` : ''}\${invoice.description}, \${invoice.amountLabel}\`}
              />
              <span className="invoice-body">
                <span className="invoice-title">
                  {invoice.typeLabel && <span className="bill-type" data-testid="bill-type">{invoice.typeLabel}</span>}
                  <strong>{invoice.description}</strong>
                </span>
                <small>Due {invoice.dueDateLabel}</small>
                {invoice.note && <small className={invoice.noteEmphasis ? 'bill-note-strong' : 'bill-note'}>{invoice.note}</small>}
              </span>
              <span className="invoice-right">
                {invoice.statusColor && (
                  <span className={\`status-dot status-\${invoice.statusColor}\`} title={invoice.statusLabel} aria-label={invoice.statusLabel} />
                )}
                <strong>{invoice.amountLabel}</strong>
              </span>
            </label>
          </li>
        ))}
      </ul>
    </section>
  );
}

export function Cart({ summary, onRemove, emptyText }: CartProps) {
  if (summary.count === 0) {
    return <section className="card cart" aria-label="Your cart" data-testid="cart"><p className="card-copy">{emptyText}</p></section>;
  }
  return (
    <section className="card cart" aria-label="Your cart" data-testid="cart">
      <h2>Your cart ({summary.count})</h2>
      <ul className="cart-lines" role="list">
        {summary.lines.map(line => (
          <li className="cart-line" key={line.id} data-testid={\`cart-line-\${line.id}\`}>
            <span>
              {line.typeLabel && <span className="bill-type">{line.typeLabel}</span>}
              {line.label}
            </span>
            <span className="cart-line-right">
              <strong>{line.amountLabel}</strong>
              {onRemove && (
                <button type="button" className="link" data-testid={\`cart-remove-\${line.id}\`} onClick={() => onRemove(line.id)} aria-label={\`Remove \${line.label} from cart\`}>
                  Remove
                </button>
              )}
            </span>
          </li>
        ))}
      </ul>
      <dl>
        <div><dt>Subtotal</dt><dd data-testid="cart-subtotal">{summary.subtotalLabel}</dd></div>
        <div><dt>Service fee</dt><dd data-testid="cart-fee">{summary.feeLabel ?? 'Calculated at checkout'}</dd></div>
        <div className="total"><dt>Total</dt><dd data-testid="cart-total">{summary.totalLabel}</dd></div>
      </dl>
    </section>
  );
}

export function BatchReview({ heading, lines, totalLabel, consentText }: BatchReviewProps) {
  return (
    <section className="card" aria-label={heading} data-testid="batch-review">
      <h2>{heading}</h2>
      <ul className="batch-lines" role="list">
        {lines.map(line => (
          <li className={\`batch-line batch-line-\${line.status}\`} key={line.id} data-testid={\`batch-line-\${line.id}\`}>
            <span className="batch-line-desc">
              {line.typeLabel && <span className="bill-type">{line.typeLabel}</span>}
              <strong>{line.label}</strong>
              {line.status !== 'pending' && (
                <small className="batch-line-status" data-testid={\`batch-status-\${line.id}\`}>
                  {line.status === 'paid' ? 'Paid' : 'Not charged'}{line.statusMessage ? \` — \${line.statusMessage}\` : ''}
                </small>
              )}
            </span>
            <span className="batch-line-amounts">
              <small>{line.amountLabel} + {line.feeLabel} fee</small>
              <strong>{line.totalLabel}</strong>
            </span>
          </li>
        ))}
      </ul>
      <dl>
        <div className="total"><dt>Total</dt><dd data-testid="batch-total">{totalLabel}</dd></div>
      </dl>
      <p className="consent">{consentText}</p>
    </section>
  );
}

export type { SelectableInvoice };
`;
}

function titleCase(value: string): string {
  return value.replace(/\b\w/g, match => match.toUpperCase());
}

// Biller-controlled strings are interpolated into generated source only inside comments.
// Strip anything that could terminate a `//` or block comment and break out into code.
function commentSafe(value: string): string {
  return value.replace(/[\r\n]+/g, ' ').replace(/\*\//g, '* /').replace(/</g, '‹').trim();
}

function hash(value: string): number {
  let h = 2166136261;
  for (let i = 0; i < value.length; i++) {
    h ^= value.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return Math.abs(h);
}
