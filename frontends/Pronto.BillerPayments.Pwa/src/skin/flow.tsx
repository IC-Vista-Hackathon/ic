import type {
  BatchReviewProps,
  CartProps,
  InvoiceSelectListProps,
  SelectableInvoice,
} from './contract';

// AI-EDITABLE SKIN FLOW STRUCTURE (feature F3).
// Presentational only. No fetch, no payment logic, no fee/total math — the stable core
// (App.tsx / provider.ts) computes every money value and hands these components fully
// preformatted view models plus callbacks. Keep the exported component names and their
// prop contracts (contract.ts) intact; the core composes these and the build/typecheck
// gate enforces the signatures. Restyle and restructure freely; never move money.

// The selectable list of open invoices a payer can add to / remove from the batch.
export function InvoiceSelectList({ heading, invoices, onToggle, onSelectAll, onClearAll, allSelected }: InvoiceSelectListProps) {
  return (
    <section className="card" aria-label={heading} data-testid="invoice-select">
      <div className="select-head">
        <h2>{heading}</h2>
        <button type="button" className="link" data-testid={allSelected ? 'clear-all' : 'select-all'} onClick={allSelected ? onClearAll : onSelectAll}>
          {allSelected ? 'Clear all' : 'Select all'}
        </button>
      </div>
      <ul className="invoice-list" role="list">
        {invoices.map(invoice => (
          <li key={invoice.id}>
            <label className="check invoice-option" data-testid={`invoice-option-${invoice.id}`}>
              <input
                type="checkbox"
                checked={invoice.selected}
                onChange={() => onToggle(invoice.id)}
                aria-label={`Pay ${invoice.typeLabel ? `${invoice.typeLabel} — ` : ''}${invoice.description}, ${invoice.amountLabel}`}
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
                  <span className={`status-dot status-${invoice.statusColor}`} title={invoice.statusLabel} aria-label={invoice.statusLabel} />
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

// The running cart summary: line items + subtotal / fee / total (all preformatted).
export function Cart({ summary, onRemove, emptyText }: CartProps) {
  if (summary.count === 0) {
    return <section className="card cart" aria-label="Your cart" data-testid="cart"><p className="card-copy">{emptyText}</p></section>;
  }
  return (
    <section className="card cart" aria-label="Your cart" data-testid="cart">
      <h2>Your cart ({summary.count})</h2>
      <ul className="cart-lines" role="list">
        {summary.lines.map(line => (
          <li className="cart-line" key={line.id} data-testid={`cart-line-${line.id}`}>
            <span>
              {line.typeLabel && <span className="bill-type">{line.typeLabel}</span>}
              {line.label}
            </span>
            <span className="cart-line-right">
              <strong>{line.amountLabel}</strong>
              {onRemove && (
                <button type="button" className="link" data-testid={`cart-remove-${line.id}`} onClick={() => onRemove(line.id)} aria-label={`Remove ${line.label} from cart`}>
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

// The batch review: one line per invoice with amount/fee/total and, during and after
// settlement, a per-invoice status. Purely presentational — the core owns settlement.
export function BatchReview({ heading, lines, totalLabel, consentText }: BatchReviewProps) {
  return (
    <section className="card" aria-label={heading} data-testid="batch-review">
      <h2>{heading}</h2>
      <ul className="batch-lines" role="list">
        {lines.map(line => (
          <li className={`batch-line batch-line-${line.status}`} key={line.id} data-testid={`batch-line-${line.id}`}>
            <span className="batch-line-desc">
              {line.typeLabel && <span className="bill-type">{line.typeLabel}</span>}
              <strong>{line.label}</strong>
              {line.status !== 'pending' && (
                <small className="batch-line-status" data-testid={`batch-status-${line.id}`}>
                  {line.status === 'paid' ? 'Paid' : 'Not charged'}{line.statusMessage ? ` — ${line.statusMessage}` : ''}
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
